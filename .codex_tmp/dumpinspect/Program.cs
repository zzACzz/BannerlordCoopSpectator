using System;
using System.Linq;
using Microsoft.Diagnostics.Runtime;

if (args.Length == 0)
{
    Console.WriteLine("Usage: dumpinspect <dump-path>");
    return;
}

string dumpPath = args[0];
using DataTarget target = DataTarget.LoadDump(dumpPath);
ClrInfo[] clrs = target.ClrVersions.ToArray();

Console.WriteLine("Dump: " + dumpPath);
Console.WriteLine("CLR count: " + clrs.Length);
for (int i = 0; i < clrs.Length; i++)
{
    ClrInfo clrInfo = clrs[i];
    Console.WriteLine($"CLR[{i}] Flavor={clrInfo.Flavor} Version={clrInfo.Version}");
}

if (clrs.Length == 0)
    return;

ClrRuntime runtime = clrs[0].CreateRuntime();
Console.WriteLine("Runtime created: " + runtime.ClrInfo.Version);

static void PrintException(ClrException? exception, string indent)
{
    if (exception == null)
        return;

    Console.WriteLine(indent + "Exception type: " + exception.Type?.Name);
    Console.WriteLine(indent + "Exception message: " + exception.Message);
    Console.WriteLine(indent + "Exception HRESULT: 0x" + exception.HResult.ToString("x8"));

    foreach (ClrStackFrame frame in exception.StackTrace)
        Console.WriteLine(indent + "  ! " + frame);

    if (exception.Inner != null)
    {
        Console.WriteLine(indent + "Inner:");
        PrintException(exception.Inner, indent + "  ");
    }
}

foreach (ClrThread thread in runtime.Threads.Where(t => t.IsAlive))
{
    Console.WriteLine($"Thread {thread.ManagedThreadId} OSThread={thread.OSThreadId:x} Alive={thread.IsAlive} Finalizer={thread.IsFinalizer} GC={thread.IsGc} Exception={(thread.CurrentException?.Type?.Name ?? "null")}");
    if (thread.CurrentException != null)
        PrintException(thread.CurrentException, "  ");

    foreach (ClrStackFrame frame in thread.EnumerateStackTrace())
    {
        Console.WriteLine("  " + frame);
    }

    Console.WriteLine();
}
