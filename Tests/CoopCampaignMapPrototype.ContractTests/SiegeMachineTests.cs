using System;
using CoopSpectator.Infrastructure;

internal static class SiegeMachineTests
{
    private static CoopCampaignMapSiegeMachine Machine(int side, string prefab) =>
        new CoopCampaignMapSiegeMachine { Side = side, Slot = 0, Prefab = prefab, InstanceId = side + 1, AnimationCycleHours = 42.5,
            Frame = new float[] { 1,0,0,0,1,0,0,0,1,1,2,3 },
            Animation = CoopCampaignMapSiegeResources.IsRanged(prefab) ?
                new CoopCampaignMapSiegeAnimation { ClipName = prefab + "_fire", Progress = 1 } : null };

    internal static void Run()
    {
        var set = new CoopCampaignMapSiegeMachines();
        Check(set.TrySnapshot(out _), "Empty set clears visuals");
        set.Items.Add(Machine(0, "ballista_b_mapicon"));
        set.Items.Add(Machine(1, "ballista_a_mapicon"));
        set.Items.Add(Machine(1, "batteringram_a_mapicon"));
        Check(set.TrySnapshot(out var copy), "Side and ranged/melee slot namespaces are separate");
        Check(CoopCampaignMapSiegeMachineCodec.TryEncode(set, out var bytes), "Encode mixed machines");
        Check(CoopCampaignMapSiegeMachineCodec.TryDecode(bytes, out var decoded), "Decode mixed machines");
        Check(decoded.Items[0].InstanceId == 1 && decoded.Items[0].AnimationCycleHours == 42.5, "Instance/cycle roundtrip");
        var badIdentity = set.Items[0].Clone(); badIdentity.InstanceId = -1;
        Check(!badIdentity.IsValid(), "Negative identity rejected");
        badIdentity.InstanceId = 0; badIdentity.AnimationCycleHours = double.NaN;
        Check(!badIdentity.IsValid(), "Invalid cycle rejected");
        Check(CoopCampaignMapSiegeMachineCodec.TryEncode(decoded, out var again) &&
            Convert.ToBase64String(bytes) == Convert.ToBase64String(again), "Exact roundtrip");
        for (int size = 0; size < bytes.Length; size++) {
            var prefix = new byte[size]; Array.Copy(bytes, prefix, size);
            Check(!CoopCampaignMapSiegeMachineCodec.TryDecode(prefix, out var incomplete) && incomplete == null,
                "Truncation rejects entire set"); }
        var excess = new byte[bytes.Length + 1]; Array.Copy(bytes, excess, bytes.Length);
        Check(!CoopCampaignMapSiegeMachineCodec.TryDecode(excess, out _), "Trailing byte");
        var invalid = (byte[])bytes.Clone(); invalid[0] ^= 1;
        Check(!CoopCampaignMapSiegeMachineCodec.TryDecode(invalid, out _), "Wrong version");
        invalid = (byte[])bytes.Clone(); Array.Copy(BitConverter.GetBytes(int.MaxValue), 0, invalid, 4, 4);
        Check(!CoopCampaignMapSiegeMachineCodec.TryDecode(invalid, out _), "Hostile count");
        invalid = (byte[])bytes.Clone(); Array.Copy(BitConverter.GetBytes(int.MaxValue), 0, invalid, 32, 4);
        Check(!CoopCampaignMapSiegeMachineCodec.TryDecode(invalid, out _), "Hostile string length");
        invalid = (byte[])bytes.Clone(); invalid[36] = 255;
        Check(!CoopCampaignMapSiegeMachineCodec.TryDecode(invalid, out _), "Non-ASCII name");
        copy.Items[0].Frame[9] = 90; copy.Items[0].Animation.Progress = 0; copy.Items.Clear();
        Check(set.Items.Count == 3 && set.Items[0].Frame[9] == 1 && set.Items[0].Animation.Progress == 1,
            "Deep snapshot owns frames and animations");
        set.Items.Add(Machine(0, "mangonel_a_mapicon"));
        Check(!set.TrySnapshot(out copy) && copy == null, "Duplicate logical slot fails atomically even with different prefab");
        set.Items.RemoveAt(3);
        foreach (int side in new[] { -1, 2 }) {
            var item = Machine(side, "ballista_a_mapicon"); Check(!item.IsValid(), "Invalid side"); }
        Check(!Machine(0, "batteringram_a_mapicon").IsValid(), "No defender melee machines");
        foreach (int slot in new[] { -1, 128, int.MaxValue }) {
            var item = Machine(1, "ballista_a_mapicon"); item.Slot = slot; Check(!item.IsValid(), "Invalid slot"); }
        foreach (float value in new[] { float.NaN, float.PositiveInfinity, 100001f }) {
            var item = Machine(1, "ballista_a_mapicon"); item.Frame[9] = value; Check(!item.IsValid(), "Invalid position"); }
        var singular = Machine(1, "ballista_a_mapicon"); singular.Frame[0] = 0;
        Check(!singular.IsValid(), "Singular frame");
        singular.Frame = new float[11]; Check(!singular.IsValid(), "Truncated frame");
        set.Items[1] = null; Check(!set.TrySnapshot(out _), "Null member");
        set.Items = null; Check(!set.TrySnapshot(out _), "Null list");
        set = new CoopCampaignMapSiegeMachines();
        for (int i = 0; i < 128; i++) { var item = Machine(1, "ballista_a_mapicon"); item.Slot = i; set.Items.Add(item); }
        Check(set.TrySnapshot(out _), "Maximum count");
        set.Items.Add(Machine(0, "ballista_b_mapicon"));
        Check(!set.TrySnapshot(out _), "Oversized set");
        Console.WriteLine("Siege machine detached model checks passed; native integration not covered.");
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception("Siege machines: " + message); }
}
