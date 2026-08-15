using System;
using CoopSpectator.Infrastructure;

internal static class Program
{
    private sealed class Snapshot
    {
        public Snapshot(string resultId)
        {
            ResultId = resultId;
        }

        public string ResultId { get; }
    }

    private static int Main()
    {
        try
        {
            ValidateUnchangedStampReturnsCachedSnapshot();
            ValidateChangedLengthMissesCache();
            ValidateChangedTimestampMissesCache();
            ValidateNewResultReplacesOldSnapshot();
            ValidateInvalidationRemovesSnapshot();
            ValidateFileChangeDuringReadIsRejected();
            Console.WriteLine("Coop battle result read cache contract tests passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static void ValidateUnchangedStampReturnsCachedSnapshot()
    {
        var cache = new CoopBattleResultReadCache<Snapshot>();
        var stamp = new CoopBattleResultFileStamp(@"C:\results\battle_result.json", 4096, 1000);
        var expected = new Snapshot("result-1");

        Assert(cache.TryStore(stamp, stamp, expected),
            "A stable read must populate the cache.");
        Assert(cache.TryGet(stamp, out Snapshot actual) && ReferenceEquals(expected, actual),
            "An unchanged file stamp must return the existing parsed snapshot.");
    }

    private static void ValidateChangedLengthMissesCache()
    {
        var cache = CreatePopulatedCache(out CoopBattleResultFileStamp originalStamp);
        var changedStamp = new CoopBattleResultFileStamp(
            originalStamp.Path,
            originalStamp.Length + 1,
            originalStamp.LastWriteUtcTicks);

        Assert(!cache.TryGet(changedStamp, out _),
            "A changed file length must force a fresh read and parse.");
    }

    private static void ValidateChangedTimestampMissesCache()
    {
        var cache = CreatePopulatedCache(out CoopBattleResultFileStamp originalStamp);
        var changedStamp = new CoopBattleResultFileStamp(
            originalStamp.Path,
            originalStamp.Length,
            originalStamp.LastWriteUtcTicks + 1);

        Assert(!cache.TryGet(changedStamp, out _),
            "A changed last-write timestamp must force a fresh read and parse.");
    }

    private static void ValidateNewResultReplacesOldSnapshot()
    {
        var cache = CreatePopulatedCache(out CoopBattleResultFileStamp originalStamp);
        var newStamp = new CoopBattleResultFileStamp(
            originalStamp.Path,
            originalStamp.Length + 512,
            originalStamp.LastWriteUtcTicks + 10);
        var newSnapshot = new Snapshot("result-2");

        Assert(cache.TryStore(newStamp, newStamp, newSnapshot),
            "A new stable result must replace the old cached snapshot.");
        Assert(cache.TryGet(newStamp, out Snapshot actual) &&
               actual.ResultId == "result-2",
            "The cache must expose the newly parsed result.");
        Assert(!cache.TryGet(originalStamp, out _),
            "The replaced file stamp must no longer return the old result.");
    }

    private static void ValidateInvalidationRemovesSnapshot()
    {
        var cache = CreatePopulatedCache(out CoopBattleResultFileStamp stamp);

        cache.Invalidate();

        Assert(!cache.TryGet(stamp, out _),
            "Clearing or removing the result file must invalidate the cached snapshot.");
    }

    private static void ValidateFileChangeDuringReadIsRejected()
    {
        var cache = new CoopBattleResultReadCache<Snapshot>();
        var beforeRead = new CoopBattleResultFileStamp(@"C:\results\battle_result.json", 4096, 1000);
        var afterRead = new CoopBattleResultFileStamp(@"C:\results\battle_result.json", 8192, 1001);

        Assert(!CoopBattleResultReadCacheContract.IsStable(beforeRead, afterRead),
            "A file that changes during the read must not be considered stable.");
        Assert(!cache.TryStore(beforeRead, afterRead, new Snapshot("partial")),
            "A snapshot read while the file is changing must not enter the cache.");
        Assert(!cache.TryGet(afterRead, out _),
            "A rejected partial read must leave the cache empty for a later retry.");
    }

    private static CoopBattleResultReadCache<Snapshot> CreatePopulatedCache(
        out CoopBattleResultFileStamp stamp)
    {
        var cache = new CoopBattleResultReadCache<Snapshot>();
        stamp = new CoopBattleResultFileStamp(@"C:\results\battle_result.json", 4096, 1000);
        Assert(cache.TryStore(stamp, stamp, new Snapshot("result-1")),
            "Test setup must populate the cache.");
        return cache;
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
