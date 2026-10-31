using System;
using CoopSpectator.Infrastructure;

internal static class ContentProfileTransportTests
{
    internal static void Run()
    {
        string profile = "naval-v1:" + new string('a', 64);
        var frame = new CoopCampaignMapPrototypeHostSnapshot {
            ContentProfileId = profile, SchemaVersion = CoopCampaignMapPrototypeContract.HostBridgeSchemaVersion,
            SessionId = "0123456789abcdef0123456789abcdef", Revision = 1, IsActive = true, UpdatedUtc = DateTime.UtcNow };
        Check(CoopCampaignMapPrototypeBridgeCodec.TryParse(CoopCampaignMapPrototypeBridgeCodec.Serialize(frame), out var decoded, out _) &&
            decoded.ContentProfileId == profile && frame.Clone().ContentProfileId == profile, "host bridge and retained frame preserve profile");
        var state = CoopCampaignMapPrototypeContract.ToNetworkState(frame, 3);
        Check(state.ContentProfileId == profile && state.Clone().ContentProfileId == profile, "network projection and client clone preserve profile");
        frame.ContentProfileId = "naval-v1:" + new string('g', 64);
        Check(!CoopCampaignMapPrototypeContract.TryValidateHostSnapshot(frame, DateTime.UtcNow, TimeSpan.Zero, out var reason) &&
            reason == "content-profile", "invalid host content identity fails validation");
        Check(!CoopCampaignContentProfile.IsValid(profile + "\n") && !CoopCampaignContentProfile.IsValid("Naval-v1:" + new string('a',64)),
            "identity syntax is canonical, bounded and newline-free");
        Console.WriteLine("PASS content profile transport: host bridge, retained/peer clone and malformed identity rejection.");
    }
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
