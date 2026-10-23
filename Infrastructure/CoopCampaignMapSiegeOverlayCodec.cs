using System;
using System.IO;
using System.Text;

namespace CoopSpectator.Infrastructure
{
    // Inner detached payload, not an authority/session envelope.
    public static class CoopCampaignMapSiegeOverlayCodec
    {
        private const int Magic = 0x43534F3A; // CSO10
        public const int MinimumBytes = 34;
        public const int MaximumBytes = MinimumBytes + 28 + 4 * CoopCampaignMapSiegeOverlay.MaximumTitleCharacters +
            49 * CoopCampaignMapSiegeOverlay.MaximumPoints + CoopCampaignMapSiegeDecalCodec.MaximumBytes +
            CoopCampaignMapSiegeMachineCodec.MaximumBytes + CoopCampaignMapSiegeProjectileCodec.MaximumBytes;
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);

        public static bool TryEncode(CoopCampaignMapSiegeOverlay value, out byte[] bytes)
        {
            bytes = null;
            if (value == null || !value.IsValid()) return false;
            try
            {
                byte[] title = Utf8.GetBytes(value.PreparationTitleText);
                using (var stream = new MemoryStream())
                using (var writer = new BinaryWriter(stream))
                {
                    writer.Write(Magic); writer.Write(title.Length); writer.Write(title);
                    writer.Write(value.PreparationProgress); writer.Write(value.IsPreparationsCompleted);
                    writer.Write(value.Points.Count);
                    writer.Write(value.CameraDistance);
                    foreach (var point in value.Points)
                    {
                        writer.Write(point.Type); writer.Write(point.MachineIndex);
                        writer.Write(point.X); writer.Write(point.Y); writer.Write(point.Z);
                        writer.Write(point.PrimaryColor); writer.Write(point.SecondaryColor);
                        writer.Write(point.MachineType); writer.Write(point.QueueIndex);
                        writer.Write(point.CurrentHitpoints); writer.Write(point.MaxHitpoints);
                        writer.Write(point.HasItem); writer.Write(point.IsConstructing);
                        writer.Write(point.IsPlayerSidePOI); writer.Write(point.IsFireVersion);
                        writer.Write(point.IsSelected);
                    }
                    byte[] decals = null;
                    if (value.Decals != null && !CoopCampaignMapSiegeDecalCodec.TryEncode(value.Decals, out decals)) return false;
                    writer.Write(decals?.Length ?? 0);
                    if (decals != null) writer.Write(decals);
                    byte[] machines = null;
                    if (value.Machines != null && !CoopCampaignMapSiegeMachineCodec.TryEncode(value.Machines, out machines)) return false;
                    writer.Write(machines?.Length ?? 0);
                    if (machines != null) writer.Write(machines);
                    byte[] projectiles = null;
                    if (value.Projectiles != null && !CoopCampaignMapSiegeProjectileCodec.TryEncode(value.Projectiles, out projectiles)) return false;
                    writer.Write(projectiles?.Length ?? 0);
                    if (projectiles != null) writer.Write(projectiles);
                    writer.Write(value.Time != null);
                    if (value.Time != null)
                    {
                        writer.Write(value.Time.Epoch); writer.Write(value.Time.SampleSeconds);
                        writer.Write(value.Time.CampaignHours); writer.Write(value.Time.TimeScale);
                    }
                    writer.Flush();
                    if (stream.Length > MaximumBytes) return false;
                    bytes = stream.ToArray(); return true;
                }
            }
            catch (ArgumentException) { return false; }
            catch (IOException) { return false; }
        }

        public static bool TryDecode(byte[] bytes, out CoopCampaignMapSiegeOverlay value)
        {
            value = null;
            if (bytes == null || bytes.Length < MinimumBytes || bytes.Length > MaximumBytes) return false;
            try
            {
                using (var stream = new MemoryStream(bytes, false))
                using (var reader = new BinaryReader(stream))
                {
                    if (reader.ReadInt32() != Magic) return false;
                    int titleBytes = reader.ReadInt32();
                    if (titleBytes < 0 || titleBytes > 4 * CoopCampaignMapSiegeOverlay.MaximumTitleCharacters ||
                        titleBytes > stream.Length - stream.Position - 13) return false;
                    var result = new CoopCampaignMapSiegeOverlay {
                        PreparationTitleText = Utf8.GetString(reader.ReadBytes(titleBytes)),
                        PreparationProgress = reader.ReadSingle(),
                        IsPreparationsCompleted = ReadBoolean(reader)
                    };
                    int count = reader.ReadInt32();
                    result.CameraDistance = reader.ReadSingle();
                    if (count < 0 || count > CoopCampaignMapSiegeOverlay.MaximumPoints ||
                        stream.Length - stream.Position < 49L * count + 13) return false;
                    for (int i = 0; i < count; i++)
                        result.Points.Add(new CoopCampaignMapSiegePoint {
                            Type = reader.ReadInt32(), MachineIndex = reader.ReadInt32(),
                            X = reader.ReadSingle(), Y = reader.ReadSingle(), Z = reader.ReadSingle(),
                            PrimaryColor = reader.ReadUInt32(), SecondaryColor = reader.ReadUInt32(),
                            MachineType = reader.ReadInt32(), QueueIndex = reader.ReadInt32(),
                            CurrentHitpoints = reader.ReadSingle(), MaxHitpoints = reader.ReadSingle(),
                            HasItem = ReadBoolean(reader), IsConstructing = ReadBoolean(reader),
                            IsPlayerSidePOI = ReadBoolean(reader), IsFireVersion = ReadBoolean(reader),
                            IsSelected = ReadBoolean(reader)
                        });
                    int decalLength = reader.ReadInt32();
                    if (decalLength < 0 || decalLength > CoopCampaignMapSiegeDecalCodec.MaximumBytes ||
                        decalLength > stream.Length - stream.Position - 9) return false;
                    if (decalLength != 0)
                    {
                        if (!CoopCampaignMapSiegeDecalCodec.TryDecode(reader.ReadBytes(decalLength), out var decals)) return false;
                        result.Decals = decals;
                    }
                    int machineLength = reader.ReadInt32();
                    if (machineLength < 0 || machineLength > CoopCampaignMapSiegeMachineCodec.MaximumBytes ||
                        machineLength > stream.Length - stream.Position - 5) return false;
                    if (machineLength != 0)
                    {
                        if (!CoopCampaignMapSiegeMachineCodec.TryDecode(reader.ReadBytes(machineLength), out var machines)) return false;
                        result.Machines = machines;
                    }
                    int projectileLength = reader.ReadInt32();
                    if (projectileLength < 0 || projectileLength > CoopCampaignMapSiegeProjectileCodec.MaximumBytes ||
                        projectileLength > stream.Length - stream.Position - 1) return false;
                    if (projectileLength != 0)
                    {
                        if (!CoopCampaignMapSiegeProjectileCodec.TryDecode(reader.ReadBytes(projectileLength), out var projectiles)) return false;
                        result.Projectiles = projectiles;
                    }
                    if (ReadBoolean(reader)) result.Time = new CoopCampaignMapSiegeTime {
                        Epoch = reader.ReadInt64(), SampleSeconds = reader.ReadDouble(),
                        CampaignHours = reader.ReadDouble(), TimeScale = reader.ReadSingle()
                    };
                    if (stream.Position != stream.Length) return false;
                    if (!result.IsValid()) return false;
                    value = result; return true;
                }
            }
            catch (ArgumentException) { return false; }
            catch (IOException) { return false; }
        }

        private static bool ReadBoolean(BinaryReader reader)
        {
            byte value = reader.ReadByte();
            if (value > 1) throw new IOException("Noncanonical siege boolean");
            return value == 1;
        }
    }
}
