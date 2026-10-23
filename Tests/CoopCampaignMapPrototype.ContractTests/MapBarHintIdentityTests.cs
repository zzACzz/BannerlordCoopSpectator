using System;
using CoopSpectator.Infrastructure;

internal static class MapBarHintIdentityTests
{
    internal static void Run()
    {
        var lease = new CoopCampaignMapBarHintIdentity<object>();
        var owner = new object(); var gold = new object(); var speed = new object();
        string session = Guid.NewGuid().ToString("N");
        void Check(bool value) { if (!value) throw new Exception("Map bar hint identity contract"); }
        Check(lease.TryUpdate(owner, session, true, new[] { "gold", "speed" }, new[] { gold, speed }, out var tokens));
        string goldToken = tokens[0], speedToken = tokens[1];
        Check(lease.TryResolve(owner, session, goldToken, out var resolved) && ReferenceEquals(resolved, gold));
        tokens[0] = "corrupted-caller-copy";
        Check(lease.TryResolve(owner, session, goldToken, out _));
        Check(lease.TryUpdate(owner, session, true, new[] { "speed", "gold" }, new[] { speed, gold }, out tokens));
        Check(tokens[0] == speedToken && tokens[1] == goldToken);
        Check(!lease.TryResolve(new object(), session, goldToken, out _));
        Check(!lease.TryResolve(owner, Guid.NewGuid().ToString("N"), goldToken, out _));
        Check(lease.TryUpdate(owner, session, true, new[] { "gold" }, new[] { new object() }, out tokens));
        Check(!lease.TryResolve(owner, session, goldToken, out _) && !lease.TryResolve(owner, session, speedToken, out _));
        goldToken = tokens[0];
        Check(!lease.TryUpdate(owner, session, false, new[] { "gold" }, new[] { gold }, out _));
        Check(!lease.TryResolve(owner, session, goldToken, out _));
        Check(lease.TryUpdate(owner, session, true, new[] { "gold" }, new[] { gold }, out tokens));
        Check(tokens[0] != goldToken);
        goldToken = tokens[0];
        Check(!lease.TryUpdate(owner, session, true, new[] { "gold", "gold" }, new[] { gold, gold }, out _));
        Check(!lease.TryResolve(owner, session, goldToken, out _));
        Check(!lease.TryUpdate(owner, session, true, new[] { "../provider" }, new[] { gold }, out _));
        Check(!lease.TryUpdate(owner, session, true, new[] { "gold" }, new object[] { null }, out _));
        Check(!CoopCampaignMapBarHintIdentity<object>.IsToken("barhint:" + new string('0', 32)));
        Console.WriteLine("Map bar hint identity: replacement, reorder, removal, hidden, session, owner and malformed-target checks passed.");
    }
}
