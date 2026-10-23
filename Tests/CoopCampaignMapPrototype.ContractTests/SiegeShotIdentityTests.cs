using System;
using CoopSpectator.Infrastructure;

internal static class SiegeShotIdentityTests
{
    private sealed class EqualShot
    {
        public override bool Equals(object other) => other is EqualShot;
        public override int GetHashCode() => 0;
    }

    internal static void Run()
    {
        var registry = new CoopCampaignMapSiegeShotIdentity<EqualShot>(3);
        var epoch = new object();
        var a = new EqualShot(); var b = new EqualShot(); var c = new EqualShot();
        Check(registry.TryAssign(epoch, new[] { a, b }, out var first), "initial");
        Check(first[0] > 0 && first[1] > first[0], "reference identity despite value equality");
        Check(registry.TryAssign(epoch, new[] { b, a }, out var reordered) &&
            reordered[0] == first[1] && reordered[1] == first[0], "reorder");
        Check(!registry.TryAssign(epoch, new[] { c, c }, out var invalid) && invalid == null, "duplicate atomic");
        Check(!registry.TryAssign(epoch, new EqualShot[] { null }, out _), "null member");
        Check(!registry.TryAssign(null, new[] { a }, out _), "missing epoch");
        Check(!registry.TryAssign(epoch, null, out _), "missing population");
        Check(!registry.TryAssign(epoch, new[] { a, b, c, new EqualShot() }, out _), "bounded");
        Check(registry.TryAssign(epoch, new[] { b, c }, out var compacted) &&
            compacted[0] == first[1] && compacted[1] == first[1] + 1, "compaction and failed batches preserve counter");
        Check(registry.TryAssign(epoch, new[] { a, b, c }, out var restored) &&
            restored[0] > compacted[1] && restored[1] == first[1], "departed object receives fresh identity");
        Check(registry.TryAssign(new object(), new[] { a, b }, out var changed) &&
            changed[0] > restored[0] && changed[1] > changed[0], "siege change");
        registry.Clear();
        Check(registry.TryAssign(epoch, new[] { a }, out var cleared) && cleared[0] > changed[1], "clear never reuses id");
        Check(registry.TryAssign(epoch, Array.Empty<EqualShot>(), out var empty) && empty.Length == 0, "empty prunes");
        Check(registry.TryAssign(epoch, new[] { a }, out var returned) && returned[0] > cleared[0], "reappearance");
        Console.WriteLine("Siege shot identity checks passed; capture/playback integration pending.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Siege shot identity: " + message);
    }
}
