using System;
using System.IO;
using System.Text;

namespace CoopSpectator.Infrastructure
{
    // Inner presentation only; requires the enclosing admitted siege/session identity.
    public static class CoopCampaignMapSiegeMachineCodec
    {
        private const int Magic = 0x43534D32; // CSM2
        public const int MinimumBytes = 8;
        public const int MaximumBytes = MinimumBytes + 345 * CoopCampaignMapSiegeMachines.MaximumCount;

        public static bool TryEncode(CoopCampaignMapSiegeMachines source, out byte[] bytes)
        {
            bytes = null;
            if (source == null || !source.TrySnapshot(out var snapshot)) return false;
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(Magic); writer.Write(snapshot.Items.Count);
                foreach (var item in snapshot.Items)
                {
                    writer.Write(item.Side); writer.Write(item.Slot);
                    writer.Write(item.InstanceId); writer.Write(item.AnimationCycleHours);
                    WriteName(writer, item.Prefab);
                    foreach (float component in item.Frame) writer.Write(component);
                    writer.Write((byte)(item.Animation == null ? 0 : 1));
                    if (item.Animation != null)
                    {
                        WriteName(writer, item.Animation.ClipName);
                        writer.Write(item.Animation.Progress); writer.Write(item.Animation.Speed);
                    }
                }
                writer.Flush();
                if (stream.Length > MaximumBytes) return false;
                bytes = stream.ToArray(); return true;
            }
        }

        public static bool TryDecode(byte[] bytes, out CoopCampaignMapSiegeMachines value)
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
                    if (count < 0 || count > CoopCampaignMapSiegeMachines.MaximumCount ||
                        stream.Length - stream.Position < (long)count * 78) return false;
                    var candidate = new CoopCampaignMapSiegeMachines();
                    for (int i = 0; i < count; i++)
                    {
                        var item = new CoopCampaignMapSiegeMachine { Side = reader.ReadInt32(), Slot = reader.ReadInt32() };
                        item.InstanceId = reader.ReadInt64(); item.AnimationCycleHours = reader.ReadDouble();
                        if (!ReadName(reader, out var prefab)) return false;
                        item.Prefab = prefab; item.Frame = new float[12];
                        for (int j = 0; j < 12; j++) item.Frame[j] = reader.ReadSingle();
                        byte flag = reader.ReadByte();
                        if (flag > 1) return false;
                        if (flag == 1)
                        {
                            if (!ReadName(reader, out var clip)) return false;
                            item.Animation = new CoopCampaignMapSiegeAnimation { ClipName = clip,
                                Progress = reader.ReadSingle(), Speed = reader.ReadSingle() };
                        }
                        candidate.Items.Add(item);
                    }
                    return stream.Position == stream.Length && candidate.TrySnapshot(out value);
                }
            }
            catch (IOException) { return false; }
        }

        private static void WriteName(BinaryWriter writer, string name)
        {
            byte[] data = Encoding.ASCII.GetBytes(name);
            writer.Write(data.Length); writer.Write(data);
        }

        private static bool ReadName(BinaryReader reader, out string name)
        {
            name = null;
            int length = reader.ReadInt32();
            if (length < 1 || length > CoopCampaignMapSiegeAnimation.MaximumNameLength ||
                length > reader.BaseStream.Length - reader.BaseStream.Position) return false;
            byte[] data = reader.ReadBytes(length);
            foreach (byte c in data)
                if (!(c >= 'a' && c <= 'z') && !(c >= 'A' && c <= 'Z') &&
                    !(c >= '0' && c <= '9') && c != '_') return false;
            name = Encoding.ASCII.GetString(data); return true;
        }
    }
}
