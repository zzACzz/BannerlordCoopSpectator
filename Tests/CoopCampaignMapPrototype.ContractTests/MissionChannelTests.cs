using System;
using System.IO;
using CoopSpectator.Infrastructure;

internal static class MissionChannelTests
{
    private const string Session = "9220ea46a9cd4110999b65d2d8653014";
    private static int _count;
    private static CoopMissionProcessIdentity Identity(int id) => new CoopMissionProcessIdentity
    { Id = id, StartUtcTicks = 639000000000000000, Path = Path.GetFullPath("synthetic-role.exe") };
    private static CoopCampaignMissionEnvelope Value() => new CoopCampaignMissionEnvelope {
        Host = Identity(111), Dedicated = Identity(222), Pulse = 1, SentTimestamp = 100000,
        CampaignId = "8324ccc7ad0b48248478e77bd9f966c87",
        Intent = new CoopCampaignMissionIntent { Session = Session, Revision = 1, Kind = CoopCampaignMissionIntentKind.Map,
            HostOnGlobalMap = true, BattleKey = "", AppliedBattleKey = "" } };
    private static void Check(bool value, string name)
    { _count++; if (!value) throw new Exception("Mission channel: " + name); }
    private static void Reject(Action action, string name)
    {
        bool failed = false;
        try { action(); } catch (IOException) { failed = true; } catch (InvalidDataException) { failed = true; }
        catch (ArgumentException) { failed = true; }
        Check(failed, name);
    }
    internal static void Run()
    {
        var value = Value();
        byte[] encoded = CoopCampaignMissionChannel.Encode(value);
        var decoded = CoopCampaignMissionChannel.Decode(encoded);
        Check(decoded.Intent.SameContent(value.Intent), "map roundtrip");
        Check(decoded.Host.Matches(value.Host) && decoded.Dedicated.Matches(value.Dedicated), "process roundtrip");
        Check(!decoded.Intent.Fresh, "wire cannot self-admit freshness");
        for (int n = 0; n < encoded.Length; n++)
        {
            byte[] truncated = new byte[n]; Array.Copy(encoded, truncated, n);
            Reject(() => CoopCampaignMissionChannel.Decode(truncated), "truncated at " + n);
        }
        byte[] trailing = new byte[encoded.Length + 1]; Array.Copy(encoded, trailing, encoded.Length);
        Reject(() => CoopCampaignMissionChannel.Decode(trailing), "trailing bytes");
        Reject(() => CoopCampaignMissionChannel.Decode(new byte[CoopCampaignMissionChannel.MaxEnvelopeBytes + 1]), "file size");
        byte[] corrupt = (byte[])encoded.Clone(); corrupt[0] ^= 1;
        Reject(() => CoopCampaignMissionChannel.Decode(corrupt), "schema");
        corrupt = (byte[])encoded.Clone(); corrupt[4] = 255; corrupt[5] = 255;
        Reject(() => CoopCampaignMissionChannel.Decode(corrupt), "string allocation bound");
        value.Intent.Session = "../previous";
        Reject(() => CoopCampaignMissionChannel.Encode(value), "invalid session");
        Reject(() => CoopCampaignMissionChannel.DirectoryFor("../outside"), "path traversal");
        value = Value(); value.Intent.Kind = (CoopCampaignMissionIntentKind)99;
        Reject(() => CoopCampaignMissionChannel.Encode(value), "unknown intent");
        value = Value(); value.Host.Id = 0;
        Reject(() => CoopCampaignMissionChannel.Encode(value), "zero pid");
        value = Value(); value.Dedicated.Path = "relative.exe";
        Reject(() => CoopCampaignMissionChannel.Encode(value), "relative executable");
        value = Value(); value.CampaignId = new string('x', 2049);
        Reject(() => CoopCampaignMissionChannel.Encode(value), "bounded text");
        value = Value(); value.Intent.Kind = CoopCampaignMissionIntentKind.Battle;
        Reject(() => CoopCampaignMissionChannel.Encode(value), "missing battle identity");
        value.BattleId = "battle-1"; value.BattleInstanceId = "instance-1"; value.Scene = "battle_terrain_001"; value.GameType = "Battle";
        value.RosterSha256 = value.Intent.BattleKey = new string('A', 64);
        Check(CoopCampaignMissionChannel.Decode(CoopCampaignMissionChannel.Encode(value)).RosterSha256 == value.RosterSha256, "battle roundtrip");
        value.Scene = "scene\nend_mission";
        Reject(() => CoopCampaignMissionChannel.Encode(value), "command injection");
        value.Scene = "battle_terrain_001"; value.Intent.BattleKey = new string('B', 64);
        Reject(() => CoopCampaignMissionChannel.Encode(value), "digest key binding");
        foreach (string token in new[] { "", "with space", "../file", "a;b", "a\r", new string('x', 129) })
            Check(!CoopCampaignMissionChannel.IsToken(token), "unsafe token");

        value = Value();
        Check(CoopCampaignMissionChannel.IsFresh(value, 115000, 1000), "fresh boundary");
        Check(!CoopCampaignMissionChannel.IsFresh(value, 115001, 1000), "expired pulse");
        Check(!CoopCampaignMissionChannel.IsFresh(value, 99999, 1000), "future pulse");
        Check(!CoopCampaignMissionChannel.IsFresh(value, 100000, 0), "invalid clock");
        var admission = new CoopCampaignMissionAdmission(Session, Identity(222));
        Check(admission.Admit(value, 100001, 1000, _ => false) == null, "dead host");
        value.Dedicated.StartUtcTicks++;
        Check(admission.Admit(value, 100001, 1000, _ => true) == null, "pid reuse");
        value.Dedicated = Identity(222); value.Intent.Session = Guid.NewGuid().ToString("N");
        Check(admission.Admit(value, 100001, 1000, _ => true) == null, "previous session");
        value = Value();
        var admitted = admission.Admit(value, 100001, 1000, _ => true);
        Check(admitted.Intent.Fresh, "fresh exact process pair");
        admitted.Host.Id = 777; // Caller cannot mutate admission history.
        Check(admission.Admit(value, 100002, 1000, _ => true) != null, "unchanged pulse within lease");
        value.Intent.HostOnGlobalMap = false;
        Reject(() => admission.Admit(value, 100003, 1000, _ => true), "reused pulse content");
        value = Value(); value.Pulse = 2; value.SentTimestamp++;
        Check(admission.Admit(value, 100004, 1000, _ => true) != null, "next pulse");
        Check(admission.Admit(Value(), 100004, 1000, _ => true) == null, "pulse rollback");
        value.Pulse = 3; value.Host.Id++;
        Reject(() => admission.Admit(value, 100005, 1000, _ => true), "host replacement");
        value.Host = Identity(111); value.CampaignId = Guid.NewGuid().ToString("N");
        Reject(() => admission.Admit(value, 100005, 1000, _ => true), "save replacement");

        string root = Path.Combine(Path.GetTempPath(), "CoopMissionChannelTest-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(root, "intent.bin");
        try
        {
            CoopCampaignMissionChannel.WriteAtomic(path, encoded);
            Check(CoopCampaignMissionChannel.Digest(CoopCampaignMissionChannel.ReadBounded(path, encoded.Length)) ==
                CoopCampaignMissionChannel.Digest(encoded), "atomic initial publication");
            Reject(() => CoopCampaignMissionChannel.ReadBounded(path, encoded.Length - 1), "bounded file read");
            value = Value(); value.Pulse = 2;
            CoopCampaignMissionChannel.WriteAtomic(path, CoopCampaignMissionChannel.Encode(value));
            Check(CoopCampaignMissionChannel.Decode(CoopCampaignMissionChannel.ReadBounded(path, 16384)).Pulse == 2, "atomic replacement");
            Check(Directory.GetFiles(root).Length == 1, "no temporary residue");
        }
        finally { if (File.Exists(path)) File.Delete(path); if (Directory.Exists(root)) Directory.Delete(root); }
        Console.WriteLine("Mission channel contracts passed: " + _count);
    }
}
