using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using CoopSpectator.Infrastructure;

internal static class TooltipTextTransactionTests
{
    private static int _count;
    internal static void Run()
    {
        int owner = Thread.CurrentThread.ManagedThreadId;
        object old = new object();
        var variables = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase) { ["Existing"] = old, ["Null"] = null };
        var evaluated = new Stack<object[]>();
        var raw = new Stack<object[]>();
        for (int cycle = 0; cycle < 100; cycle++)
        {
            Check(CoopCampaignMapTooltipTextTransaction<object>.TryBegin(variables, evaluated, raw, owner, out var scope), "begin");
            try
            {
                using (scope)
                {
                    variables["existing"] = new object();
                    variables.Remove("Null");
                    variables["EXTEND_KEY"] = "Alt";
                    evaluated.Push(new object[] { "stranded" });
                    raw.Push(new object[] { "raw" });
                    if ((cycle & 1) != 0) throw new InvalidOperationException("Synthetic provider failure");
                }
            }
            catch (InvalidOperationException) { }
            Check(variables.Count == 2 && ReferenceEquals(variables["existing"], old) && variables["Null"] == null,
                "exact entries and references after success/failure");
            Check(variables.Keys.Any(key => string.Equals(key, "Existing", StringComparison.Ordinal)) &&
                !variables.Keys.Any(key => string.Equals(key, "existing", StringComparison.Ordinal)), "original key spelling");
            Check(evaluated.Count == 0 && raw.Count == 0, "failed native parser stacks cleared");
            variables["after"] = "keep";
            scope.Dispose();
            Check(variables.ContainsKey("after"), "idempotent dispose does not undo later work");
            variables.Remove("after");
        }
        evaluated.Push(new object[0]);
        Check(!CoopCampaignMapTooltipTextTransaction<object>.TryBegin(variables, evaluated, raw, owner, out _) && evaluated.Count == 1,
            "busy evaluated stack is not repaired");
        evaluated.Clear(); raw.Push(new object[0]);
        Check(!CoopCampaignMapTooltipTextTransaction<object>.TryBegin(variables, evaluated, raw, owner, out _) && raw.Count == 1,
            "busy raw stack is not repaired");
        raw.Clear();
        Check(!CoopCampaignMapTooltipTextTransaction<object>.TryBegin(null, evaluated, raw, owner, out _), "missing variables");
        Check(!CoopCampaignMapTooltipTextTransaction<object>.TryBegin(variables, null, raw, owner, out _), "missing evaluated stack");
        Check(!CoopCampaignMapTooltipTextTransaction<object>.TryBegin(variables, evaluated, null, owner, out _), "missing raw stack");
        Check(!CoopCampaignMapTooltipTextTransaction<object>.TryBegin(variables, evaluated, raw, 0, out _), "invalid owner");
        bool otherBegan = true;
        var worker = new Thread(() => otherBegan = CoopCampaignMapTooltipTextTransaction<object>.TryBegin(variables, evaluated, raw, owner, out _));
        worker.Start(); worker.Join();
        Check(!otherBegan, "worker cannot capture");
        Check(CoopCampaignMapTooltipTextTransaction<object>.TryBegin(variables, evaluated, raw, owner, out var owned), "owned begin");
        variables["temporary"] = "x";
        bool otherDisposed = false;
        worker = new Thread(() => { try { owned.Dispose(); otherDisposed = true; } catch (InvalidOperationException) { } });
        worker.Start(); worker.Join();
        Check(!otherDisposed && variables.ContainsKey("temporary"), "worker cannot change global state during dispose");
        owned.Dispose();
        Check(!variables.ContainsKey("temporary") && ReferenceEquals(variables["Existing"], old), "owner still restores");
        var many = new Dictionary<string, object>();
        for (int i = 0; i <= CoopCampaignMapTooltipTextTransaction<object>.MaxVariables; i++) many[i.ToString()] = old;
        Check(!CoopCampaignMapTooltipTextTransaction<object>.TryBegin(many, evaluated, raw, owner, out _) && many.Count == 8193,
            "oversized context rejected without mutation");

        string root = Environment.GetEnvironmentVariable("COOPSPECTATOR_REPOSITORY_ROOT");
        string capture = File.ReadAllText(Path.Combine(root, "Campaign", "CoopCampaignMapTooltipCapture.cs"));
        Check(!capture.Contains("InformationManager.ShowTooltip(") && !capture.Contains(".OnHover(") &&
            !capture.Contains("MBTextManager.ClearAll(") && !capture.Contains("IsInspected ="), "no host UI/disclosure mutation calls");
        Check(capture.Contains("registered.Method != provider.Method") && capture.Contains("registered.Target != provider.Target"),
            "native provider compatibility checked");
        Check(capture.Contains("typeof(CaptureContext), Array.Empty<object>()") &&
            capture.IndexOf("vm.IsExtended = extended", StringComparison.Ordinal) < capture.IndexOf("provider(vm, args)", StringComparison.Ordinal),
            "single native invocation with preselected extension");
        Check(capture.Contains("new object[] { army, false, true }") && capture.Contains("new object[] { target.MobileParty, false, true }"),
            "native map disclosure arguments preserved");
        Check(capture.Contains("textScope?.Dispose()") && capture.Contains("vm?.OnFinalize()") && capture.Contains("finally"),
            "finally-bound cleanup");
        Check(capture.Contains("_curParamsWithoutEvaluate") && capture.Contains("_curParams") && capture.Contains("_variables"),
            "exact installed localization layout");
        Check(capture.Contains("_cleanupFaulted = true") && capture.Contains("payload = null") &&
            capture.LastIndexOf("return success", StringComparison.Ordinal) > capture.IndexOf("tooltip-cleanup-failed", StringComparison.Ordinal),
            "failed cleanup suppresses publication and future capture");
        Check(capture.IndexOf("IsValidCorrelation(", StringComparison.Ordinal) < capture.IndexOf("TryBeginTextScope(out", StringComparison.Ordinal),
            "invalid request cannot enter native capture");
        Check(capture.Contains("tooltip-key-text-not-ready"), "missing native key text fails closed");
        Console.WriteLine("Tooltip text transaction assertions passed: " + _count);
    }

    private static void Check(bool condition, string name)
    {
        _count++;
        if (!condition) throw new InvalidOperationException("Tooltip text transaction: " + name);
    }
}
