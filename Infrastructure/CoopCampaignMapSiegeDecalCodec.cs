using System.IO;

namespace CoopSpectator.Infrastructure
{
    // Inner presentation payload only; requires an admitted outer session/siege envelope.
    public static class CoopCampaignMapSiegeDecalCodec
    {
        private const int Magic = 0x43534431; // CSD1
        public const int MinimumBytes = 8;
        public const int RecordBytes = 60;
        public const int MaximumBytes = MinimumBytes + RecordBytes * CoopCampaignMapSiegeDecals.MaximumCount;

        public static bool TryEncode(CoopCampaignMapSiegeDecals source, out byte[] bytes)
        {
            bytes = null;
            if (source == null || !source.TrySnapshot(out var snapshot)) return false;
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(Magic); writer.Write(snapshot.Items.Count);
                foreach (var item in snapshot.Items)
                {
                    writer.Write(item.Group); writer.Write(item.Index); writer.Write(item.Color);
                    foreach (float component in item.Frame) writer.Write(component);
                }
                writer.Flush(); bytes = stream.ToArray(); return true;
            }
        }

        public static bool TryDecode(byte[] bytes, out CoopCampaignMapSiegeDecals value)
        {
            value = null;
            if (bytes == null || bytes.Length < MinimumBytes || bytes.Length > MaximumBytes) return false;
            using (var stream = new MemoryStream(bytes, false))
            using (var reader = new BinaryReader(stream))
            {
                if (reader.ReadInt32() != Magic) return false;
                int count = reader.ReadInt32();
                if (count < 0 || count > CoopCampaignMapSiegeDecals.MaximumCount ||
                    stream.Length - stream.Position != (long)RecordBytes * count) return false;
                var candidate = new CoopCampaignMapSiegeDecals();
                for (int i = 0; i < count; i++)
                {
                    var item = new CoopCampaignMapSiegeDecal
                    { Group = reader.ReadInt32(), Index = reader.ReadInt32(), Color = reader.ReadUInt32(), Frame = new float[12] };
                    for (int j = 0; j < item.Frame.Length; j++) item.Frame[j] = reader.ReadSingle();
                    candidate.Items.Add(item);
                }
                return candidate.TrySnapshot(out value);
            }
        }
    }
}
