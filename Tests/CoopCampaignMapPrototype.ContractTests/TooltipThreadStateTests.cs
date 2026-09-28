using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using CoopSpectator.Infrastructure;

internal static class TooltipThreadStateTests
{
    private enum Gender { None, Other }
    [ThreadStatic] private static bool _flag;
    [ThreadStatic] private static Gender _gender;
    [ThreadStatic] private static List<int> _markers;
    [ThreadStatic] private static List<string> _links;
    [ThreadStatic] private static List<ValueTuple<string, int>> _groups;
    [ThreadStatic] private static Dictionary<string, ValueTuple<string, int>> _pairs;
    [ThreadStatic] private static Dictionary<string, ValueTuple<string, int, bool>> _triples;
    [ThreadStatic] private static object _unsupported;
    private static List<int> _notThreadLocal = new List<int>();
    private static int _count;

    internal static void Run()
    {
        int owner = Thread.CurrentThread.ManagedThreadId;
        var fields = new[] { Field("_flag"), Field("_gender"), Field("_markers"), Field("_links"), Field("_groups"), Field("_pairs"), Field("_triples") };
        for (int cycle = 0; cycle < 100; cycle++)
        {
            _flag = true; _gender = Gender.Other;
            var markers = _markers = new List<int> { 17 };
            var links = _links = new List<string> { "host" };
            var groups = _groups = new List<ValueTuple<string, int>> { ValueTuple.Create("host", 3) };
            var pairs = _pairs = new Dictionary<string, ValueTuple<string, int>>(StringComparer.OrdinalIgnoreCase) { ["Host"] = ValueTuple.Create("text", 9) };
            var triples = _triples = new Dictionary<string, ValueTuple<string, int, bool>> { ["host"] = ValueTuple.Create("text", 2, true) };
            Check(CoopCampaignMapTooltipThreadStateScope.TryBegin(fields, owner, out var scope), "begin");
            try
            {
                using (scope)
                {
                    Check(!ReferenceEquals(markers, _markers) && !ReferenceEquals(links, _links) && !ReferenceEquals(groups, _groups) &&
                        !ReferenceEquals(pairs, _pairs) && !ReferenceEquals(triples, _triples), "independent working collections");
                    Check(_pairs.ContainsKey("HOST") && _groups[0].Item1 == "host", "content and comparer copied");
                    _flag = false; _gender = Gender.None; _markers.Clear(); _links.Add("client"); _groups.Clear(); _pairs.Clear(); _triples.Clear();
                    Check(markers.Count == 1 && links.Count == 1 && groups.Count == 1 && pairs.Count == 1 && triples.Count == 1,
                        "host collections untouched during capture");
                    if ((cycle & 1) != 0) throw new InvalidOperationException("injected formatter failure");
                }
            }
            catch (InvalidOperationException ex) when (ex.Message == "injected formatter failure") { }
            Check(_flag && _gender == Gender.Other && ReferenceEquals(_markers, markers) && ReferenceEquals(_links, links) &&
                ReferenceEquals(_groups, groups) && ReferenceEquals(_pairs, pairs) && ReferenceEquals(_triples, triples), "exact references/scalars restored");
            _flag = false; scope.Dispose(); Check(!_flag, "dispose is idempotent");
        }
        _links = null;
        Check(CoopCampaignMapTooltipThreadStateScope.TryBegin(new[] { Field("_links") }, owner, out var nullScope), "null baseline");
        _links = new List<string> { "created during capture" }; nullScope.Dispose();
        Check(_links == null, "null restored");
        var baseline = _markers;
        foreach (var invalid in new[] { new[] { Field("_markers"), Field("_unsupported") }, new[] { Field("_notThreadLocal") },
            new[] { Field("_markers"), Field("_markers") }, new FieldInfo[] { null }, Array.Empty<FieldInfo>(), new FieldInfo[9] })
        {
            Check(!CoopCampaignMapTooltipThreadStateScope.TryBegin(invalid, owner, out var denied) && denied == null &&
                ReferenceEquals(baseline, _markers), "invalid input rejected before any write");
        }
        _links = new List<string>(new string[4097]);
        Check(!CoopCampaignMapTooltipThreadStateScope.TryBegin(new[] { Field("_markers"), Field("_links") }, owner, out _) &&
            ReferenceEquals(baseline, _markers), "bounded clone preflight");
        _links = null;
        bool workerAccepted = true;
        var worker = new Thread(() => workerAccepted = CoopCampaignMapTooltipThreadStateScope.TryBegin(fields, owner, out _));
        worker.Start(); worker.Join(); Check(!workerAccepted, "worker rejected");
        Check(CoopCampaignMapTooltipThreadStateScope.TryBegin(fields, owner, out var owned), "owner begin");
        bool workerDisposed = false;
        worker = new Thread(() => { try { owned.Dispose(); workerDisposed = true; } catch (InvalidOperationException) { } });
        worker.Start(); worker.Join(); Check(!workerDisposed, "worker restore rejected");
        owned.Dispose(); Check(ReferenceEquals(baseline, _markers), "owner restore after worker rejection");
        _unsupported = new object(); // Keep unsupported reference-field coverage explicit.
        Console.WriteLine("Tooltip grammar thread-state assertions passed: " + _count);
    }

    private static FieldInfo Field(string name) => typeof(TooltipThreadStateTests).GetField(name, BindingFlags.NonPublic | BindingFlags.Static);
    private static void Check(bool condition, string name)
    {
        _count++;
        if (!condition) throw new InvalidOperationException("Tooltip grammar state: " + name);
    }
}
