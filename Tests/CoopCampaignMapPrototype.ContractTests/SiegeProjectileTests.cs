using System;
using CoopSpectator.Infrastructure;

internal static class SiegeProjectileTests
{
    internal static void Run()
    {
        var set = new CoopCampaignMapSiegeProjectiles();
        Check(set.TrySnapshot(out _), "empty removal");
        foreach (string name in new[] { "mangonel_mapicon_projectile", "mangonel_fire_mapicon_projectile", "ballista_mapicon_projectile", "ballista_fire_mapicon_projectile" })
            set.Items.Add(new CoopCampaignMapSiegeProjectile { Side = 1, Index = set.Items.Count, ShotId = set.Items.Count + 1,
                Prefab = name, Frame = new float[] {1,0,0,0,1,0,0,0,1,12,23,34}, FlightChildVisible = set.Items.Count % 2 == 0 });
        Check(set.TrySnapshot(out var copy), "snapshot");
        copy.Items[0].Flight = new CoopCampaignMapSiegeFlight {
            StartHours = 10, EndHours = 11, Origin = new float[] { 1,2,3 },
            Forward = new float[] { 0,1,0 }, Up = new float[] { 0,0,1 }, Speed = 14, Gravity = 10
        };
        set.Items[0].Frame[9] = 99;
        Check(copy.Items[0].Frame[9] == 12, "detached frame");
        Check(CoopCampaignMapSiegeProjectileCodec.TryEncode(copy, out var bytes), "encode");
        Check(CoopCampaignMapSiegeProjectileCodec.TryDecode(bytes, out var decoded), "decode");
        Check(decoded.Items[0].FlightChildVisible && !decoded.Items[1].FlightChildVisible, "separate flight visibility");
        Check(decoded.Items[0].ShotId == copy.Items[0].ShotId, "stable shot identity roundtrip");
        Check(decoded.Items[0].Flight != null && decoded.Items[1].Flight == null &&
            decoded.Items[0].Flight.EndHours == 11, "optional trajectory survives projectile wire");
        var identitySample = copy.Items[0].Clone();
        long originalKey = identitySample.Key;
        identitySample.Index = 100;
        Check(identitySample.Key == originalKey, "list compaction cannot change rendering identity");
        identitySample.ShotId = 0;
        Check(!identitySample.IsValid(), "missing shot identity rejected");
        identitySample.ShotId = -1;
        Check(!identitySample.IsValid(), "negative shot identity rejected");
        var duplicatePosition = copy.Items[0].Clone(); duplicatePosition.ShotId = 999;
        copy.Items.Add(duplicatePosition);
        Check(copy.TrySnapshot(out _), "retained distinct shots may share a historical list position");
        var savedFlight = copy.Items[0].Flight;
        copy.Items[0].Flight = null; duplicatePosition.Flight = null;
        Check(!copy.TrySnapshot(out _), "untracked distinct IDs cannot occupy one current list position");
        copy.Items[0].Flight = savedFlight;
        copy.Items.RemoveAt(copy.Items.Count - 1);
        Check(CoopCampaignMapSiegeProjectileCodec.TryEncode(decoded, out var again) && Convert.ToBase64String(bytes) == Convert.ToBase64String(again), "exact bytes");
        for (int n = 0; n < bytes.Length; n++) {
            var prefix = new byte[n]; Array.Copy(bytes, prefix, n);
            Check(!CoopCampaignMapSiegeProjectileCodec.TryDecode(prefix, out var rejected) && rejected == null, "truncation");
        }
        var invalid = (byte[])bytes.Clone(); invalid[invalid.Length - 1] = 2;
        Check(!CoopCampaignMapSiegeProjectileCodec.TryDecode(invalid, out _), "strict flag");
        invalid = new byte[bytes.Length + 1]; Array.Copy(bytes, invalid, bytes.Length);
        Check(!CoopCampaignMapSiegeProjectileCodec.TryDecode(invalid, out _), "trailing data");
        copy.Items.Add(copy.Items[0].Clone());
        Check(!copy.TrySnapshot(out _), "duplicate identity");
        copy.Items.RemoveAt(copy.Items.Count - 1);
        copy.Items[0].Frame[0] = float.NaN;
        Check(!copy.TrySnapshot(out _), "invalid frame");
        copy.Items[0].Frame[0] = 1;
        copy.Items[0].Prefab = "ballista_a_mapicon";
        Check(!copy.TrySnapshot(out _), "machine is not projectile");
        foreach (int offset in new[] { 4, 24 })
        {
            invalid = (byte[])bytes.Clone();
            Array.Copy(BitConverter.GetBytes(int.MaxValue), 0, invalid, offset, 4);
            Check(!CoopCampaignMapSiegeProjectileCodec.TryDecode(invalid, out var bad) && bad == null,
                "hostile count/name length rejected before allocation");
        }
        invalid = (byte[])bytes.Clone(); invalid[28] = 255;
        Check(!CoopCampaignMapSiegeProjectileCodec.TryDecode(invalid, out _), "non-ASCII resource");
        invalid = (byte[])bytes.Clone(); invalid[0] ^= 1;
        Check(!CoopCampaignMapSiegeProjectileCodec.TryDecode(invalid, out _), "wrong version");
        var maximum = new CoopCampaignMapSiegeProjectiles();
        for (int i = 0; i < CoopCampaignMapSiegeProjectiles.MaximumCount; i++)
        {
            var item = set.Items[0].Clone(); item.Index = i; item.ShotId = i + 1;
            maximum.Items.Add(item);
        }
        Check(CoopCampaignMapSiegeProjectileCodec.TryEncode(maximum, out var full) &&
            CoopCampaignMapSiegeProjectileCodec.TryDecode(full, out var fullCopy) &&
            fullCopy.Items.Count == CoopCampaignMapSiegeProjectiles.MaximumCount, "maximum roundtrip");
        maximum.Items.Add(set.Items[0].Clone());
        Check(!maximum.TrySnapshot(out _), "oversized set");
        maximum.Items.Clear();
        maximum.Items.Add(set.Items[0].Clone());
        var opposite = set.Items[0].Clone(); opposite.Side = 0; opposite.ShotId = 1000;
        maximum.Items.Add(opposite);
        Check(maximum.TrySnapshot(out _), "same index on opposite sides");
        opposite.Side = -1;
        Check(!maximum.TrySnapshot(out _), "negative side");
        opposite.Side = 2;
        Check(!maximum.TrySnapshot(out _), "unknown side");
        opposite.Side = 0; opposite.Index = -1;
        Check(!maximum.TrySnapshot(out _), "negative index");
        opposite.Index = CoopCampaignMapSiegeProjectiles.MaximumCount;
        Check(!maximum.TrySnapshot(out _), "index bound");
        maximum.Items = null;
        Check(!maximum.TrySnapshot(out _), "null list");
        Console.WriteLine("Siege projectile model/codec checks passed; native flight acceptance pending.");
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception("Siege projectile: " + message); }
}
