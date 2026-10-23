using System;
using CoopSpectator.Infrastructure;

internal static class SiegeDecalTests
{
    internal static void Run()
    {
        var state = new CoopCampaignMapSiegeDecals();
        Check(state.TrySnapshot(out _), "Empty replacement clears departed decals");
        for (int group = 0; group < 4; group++) state.Items.Add(new CoopCampaignMapSiegeDecal
        { Group = group, Index = 0, Color = 0xff123456, Frame = new float[] { 1,0,0,0,1,0,0,0,1,20,30,4 } });
        Check(state.TrySnapshot(out var copy), "All native groups admitted with same slot index");
        Check(CoopCampaignMapSiegeDecalCodec.TryEncode(state, out var bytes), "Encode valid set");
        Check(CoopCampaignMapSiegeDecalCodec.TryDecode(bytes, out var decoded), "Decode valid set");
        Check(CoopCampaignMapSiegeDecalCodec.TryEncode(decoded, out var roundtrip) &&
            Convert.ToBase64String(bytes) == Convert.ToBase64String(roundtrip), "Every field roundtrips exactly");
        for (int length = 0; length < bytes.Length; length++)
        {
            var prefix = new byte[length]; Array.Copy(bytes, prefix, length);
            Check(!CoopCampaignMapSiegeDecalCodec.TryDecode(prefix, out _), "Every truncated prefix rejected");
        }
        var extra = new byte[bytes.Length + 1]; Array.Copy(bytes, extra, bytes.Length);
        Check(!CoopCampaignMapSiegeDecalCodec.TryDecode(extra, out _), "Trailing byte rejected");
        var invalid = (byte[])bytes.Clone(); invalid[0] ^= 1;
        Check(!CoopCampaignMapSiegeDecalCodec.TryDecode(invalid, out _), "Wrong magic rejected");
        invalid = (byte[])bytes.Clone(); Array.Copy(BitConverter.GetBytes(int.MaxValue), 0, invalid, 4, 4);
        Check(!CoopCampaignMapSiegeDecalCodec.TryDecode(invalid, out _), "Hostile count rejected before allocation");
        invalid = (byte[])bytes.Clone(); Array.Copy(BitConverter.GetBytes(float.NaN), 0, invalid, 20, 4);
        Check(!CoopCampaignMapSiegeDecalCodec.TryDecode(invalid, out var rejected) && rejected == null,
            "Invalid transform fails without partial output");
        Check(copy.Items[0].MaterialName == "decal_defender_ranged_siege" &&
            copy.Items[1].MaterialName == "decal_siege_ram" &&
            copy.Items[2].MaterialName == "decal_siege_tower" &&
            copy.Items[3].MaterialName == "decal_siege_ranged", "Closed native material mapping");
        copy.Items[0].Frame[9] = 99; copy.Items[0].Color = 0; copy.Items.Clear();
        Check(state.Items.Count == 4 && state.Items[0].Frame[9] == 20 &&
            state.Items[0].Color == 0xff123456, "List, object and frame isolation");
        state.Items.Add(state.Items[0].Clone());
        Check(!state.TrySnapshot(out copy) && copy == null, "Duplicate fails atomically");
        state.Items.RemoveAt(4);
        foreach (float invalidCoordinate in new[] { float.NaN, float.PositiveInfinity, 100001f })
        {
            state.TrySnapshot(out copy); copy.Items[0].Frame[9] = invalidCoordinate;
            Check(!copy.TrySnapshot(out _), "Invalid translation rejected");
        }
        state.TrySnapshot(out copy); copy.Items[0].Frame[0] = 0;
        Check(!copy.TrySnapshot(out _), "Singular frame rejected");
        state.TrySnapshot(out copy); copy.Items[0].Group = 4;
        Check(!copy.TrySnapshot(out _), "Unknown group rejected");
        state.TrySnapshot(out copy); copy.Items[0].Index = -1;
        Check(!copy.TrySnapshot(out _), "Negative index rejected");
        state.TrySnapshot(out copy); copy.Items[0].Frame = new float[11];
        Check(!copy.TrySnapshot(out _), "Wrong frame length rejected");
        state.Items[0] = null;
        Check(!state.TrySnapshot(out _), "Null item rejected");
        state.Items = null;
        Check(!state.TrySnapshot(out _), "Null list rejected");
        var maximum = new CoopCampaignMapSiegeDecals();
        for (int i = 0; i < CoopCampaignMapSiegeDecals.MaximumCount; i++)
            maximum.Items.Add(new CoopCampaignMapSiegeDecal { Group = 0, Index = i,
                Frame = new float[] { 1,0,0,0,1,0,0,0,1,0,0,0 } });
        Check(CoopCampaignMapSiegeDecalCodec.TryEncode(maximum, out var largest) &&
            largest.Length == CoopCampaignMapSiegeDecalCodec.MaximumBytes &&
            CoopCampaignMapSiegeDecalCodec.TryDecode(largest, out _), "Maximum set roundtrip");
        maximum.Items.Add(maximum.Items[0].Clone());
        Check(!CoopCampaignMapSiegeDecalCodec.TryEncode(maximum, out _), "Oversized set rejected");
    }
    private static void Check(bool value, string message)
    { if (!value) throw new Exception("Siege decal: " + message); }
}
