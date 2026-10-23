using System;
using System.IO;
using System.Text;

namespace CoopSpectator.Infrastructure
{
    // Inner payload only; enclosing admission must bind siege/session and freshness.
    public static class CoopCampaignMapSiegeProjectileCodec
    {
        private const int Magic = 0x43535034; // CSP4
        public const int MinimumBytes = 8;
        public const int MaximumBytes = MinimumBytes + 279 * CoopCampaignMapSiegeProjectiles.MaximumCount;

        public static bool TryEncode(CoopCampaignMapSiegeProjectiles source, out byte[] bytes)
        {
            bytes = null;
            if (source == null || !source.TrySnapshot(out var snapshot)) return false;
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(Magic); writer.Write(snapshot.Items.Count);
                foreach (var item in snapshot.Items)
                {
                    writer.Write(item.Side); writer.Write(item.Index);
                    writer.Write(item.ShotId);
                    byte[] name = Encoding.ASCII.GetBytes(item.Prefab);
                    writer.Write(name.Length); writer.Write(name);
                    foreach (float component in item.Frame) writer.Write(component);
                    writer.Write((byte)(item.FlightChildVisible ? 1 : 0));
                    writer.Write((byte)(item.Flight != null ? (item.TrajectoryOnly ? 3 : 1) : 0));
                    if (item.Flight != null)
                    {
                        if (!CoopCampaignMapSiegeFlightCodec.TryEncode(item.Flight, out var flightBytes)) return false;
                        writer.Write(flightBytes);
                    }
                }
                writer.Flush();
                if (stream.Length > MaximumBytes) return false;
                bytes = stream.ToArray(); return true;
            }
        }

        public static bool TryDecode(byte[] bytes, out CoopCampaignMapSiegeProjectiles value)
        {
            value = null;
            if (bytes == null || bytes.Length < MinimumBytes || bytes.Length > MaximumBytes) return false;
            try
            {
                using (var stream = new MemoryStream(bytes, false))
                using (var reader = new BinaryReader(stream))
                {
                    if (reader.ReadInt32() != Magic) return false;
                    int count = reader.ReadInt32();
                    if (count < 0 || count > CoopCampaignMapSiegeProjectiles.MaximumCount ||
                        stream.Length - stream.Position < (long)count * 71) return false;
                    var candidate = new CoopCampaignMapSiegeProjectiles();
                    for (int i = 0; i < count; i++)
                    {
                        var item = new CoopCampaignMapSiegeProjectile { Side = reader.ReadInt32(), Index = reader.ReadInt32() };
                        item.ShotId = reader.ReadInt64();
                        int length = reader.ReadInt32();
                        if (length < 1 || length > 128 || length > stream.Length - stream.Position - 49) return false;
                        byte[] name = reader.ReadBytes(length);
                        foreach (byte c in name)
                            if (!(c >= 'a' && c <= 'z') && !(c >= '0' && c <= '9') && c != '_') return false;
                        item.Prefab = Encoding.ASCII.GetString(name);
                        item.Frame = new float[12];
                        for (int j = 0; j < 12; j++) item.Frame[j] = reader.ReadSingle();
                        byte flag = reader.ReadByte();
                        if (flag > 1) return false;
                        item.FlightChildVisible = flag == 1;
                        byte hasFlight = reader.ReadByte();
                        if (hasFlight != 0 && hasFlight != 1 && hasFlight != 3) return false;
                        item.TrajectoryOnly = hasFlight == 3;
                        if (hasFlight != 0)
                        {
                            if (!CoopCampaignMapSiegeFlightCodec.TryDecode(reader.ReadBytes(CoopCampaignMapSiegeFlightCodec.PayloadBytes), out var flight)) return false;
                            item.Flight = flight;
                        }
                        candidate.Items.Add(item);
                    }
                    return stream.Position == stream.Length && candidate.TrySnapshot(out value);
                }
            }
            catch (IOException) { return false; }
        }
    }
}
