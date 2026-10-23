using System;
using CoopSpectator.Infrastructure;

internal static class PortraitPoseEvidenceTests
{
    private static int _checks;
    private static void Check(bool value, string label)
    { _checks++; if (!value) throw new Exception("Portrait pose evidence: " + label); }

    internal static void Run()
    {
        var cache = new CoopPortraitPoseEvidence(2);
        Check(!cache.TryGet("hero-small", out var missing) && missing == -1, "unknown is not guessed");
        long first = cache.Begin("hero-small");
        Check(first > 0 && !cache.TryGet("hero-small", out _), "pending is not evidence");
        Check(cache.Complete("hero-small", first, 3), "successful observation");
        Check(cache.TryGet("hero-small", out var pose) && pose == 3, "cache hit preserves pose");
        Check(cache.Complete("hero-small", first, 3), "same completion idempotent");
        Check(!cache.Complete("hero-small", first, 4) && cache.TryGet("hero-small", out pose) && pose == 3,
            "discarded second selection cannot overwrite");
        long large = cache.Begin("hero-large");
        Check(cache.Complete("hero-large", large, 7), "size-separated entry");
        Check(cache.Begin("other") == 0 && cache.TryGet("hero-small", out pose) && pose == 3,
            "bounded capacity does not corrupt existing evidence");
        long replacement = cache.Begin("hero-small");
        Check(replacement > first && !cache.TryGet("hero-small", out _), "rebuild invalidates previous pose");
        cache.Cancel("hero-small", first);
        Check(!cache.Complete("hero-small", first, 2), "old completion rejected");
        Check(cache.Complete("hero-small", replacement, 1), "old cancel cannot cancel replacement");
        cache.Clear();
        long afterClear = cache.Begin("hero-small");
        Check(afterClear > replacement && !cache.Complete("hero-small", replacement, 1), "clear rejects late callback");
        Check(cache.Complete("hero-small", afterClear, 6), "new generation usable");
        cache.Cancel("hero-small", afterClear);
        Check(!cache.TryGet("hero-small", out _), "cancellation removes evidence");
        long evicted = cache.Begin("evicted");
        Check(cache.Complete("evicted", evicted, 2), "eviction setup");
        cache.Forget("evicted");
        Check(!cache.TryGet("evicted", out _) && !cache.Complete("evicted", evicted, 2),
            "native eviction invalidates completed and late evidence");
        for (int i = -1; i <= 8; i++)
        {
            long ticket = cache.Begin("pose");
            bool valid = i >= 0 && i <= 7;
            Check(cache.Complete("pose", ticket, i) == valid, "finite pose admission " + i);
            Check(cache.TryGet("pose", out pose) == valid, "finite pose lookup " + i);
            if (valid) Check(pose == i, "exact pose " + i);
        }
        Check(cache.Begin(null) == 0 && cache.Begin("") == 0 &&
            cache.Begin(new string('x', CoopPortraitPoseEvidence.MaxRenderKeyCharacters + 1)) == 0,
            "bounded key admission");
        long closing = cache.Begin("close");
        cache.Close(); cache.Clear();
        Check(!cache.Complete("close", closing, 0) && cache.Begin("close") == 0 &&
            !cache.TryGet("close", out _), "finalization is irreversible");
        var separate = new CoopPortraitPoseEvidence();
        Check(!separate.TryGet("hero-small", out _), "cache instances do not share evidence");
        Console.WriteLine("Portrait pose evidence checks passed: " + _checks);
    }
}
