using System;
using System.IO;

namespace CoopSpectator.Infrastructure
{
    // Fixed-size detached trajectory only. Enclosing shot/session/resource admission
    // is mandatory; this payload alone never authorizes rendering.
    public static class CoopCampaignMapSiegeFlightCodec
    {
        private const int Magic = 0x43534632; // CSF2
        public const int PayloadBytes = 81;
        public static bool TryEncode(CoopCampaignMapSiegeFlight flight, out byte[] bytes)
        {
            bytes = null;
            var copy = flight?.Clone();
            if (copy == null || !copy.IsValid()) return false;
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(Magic); writer.Write(copy.StartHours); writer.Write(copy.EndHours);
                foreach (float value in copy.Origin) writer.Write(value);
                foreach (float value in copy.Forward) writer.Write(value);
                foreach (float value in copy.Up) writer.Write(value);
                writer.Write(copy.Speed); writer.Write(copy.LaunchAngle); writer.Write(copy.Gravity);
                writer.Write((byte)(copy.PresentationScale == null ? 0 : 1));
                foreach (float value in copy.PresentationScale ?? new float[3]) writer.Write(value);
                writer.Flush(); bytes = stream.ToArray();
                return bytes.Length == PayloadBytes;
            }
        }
        public static bool TryDecode(byte[] bytes, out CoopCampaignMapSiegeFlight flight)
        {
            flight = null;
            if (bytes == null || bytes.Length != PayloadBytes) return false;
            try
            {
                using (var stream = new MemoryStream(bytes, false))
                using (var reader = new BinaryReader(stream))
                {
                    if (reader.ReadInt32() != Magic) return false;
                    var result = new CoopCampaignMapSiegeFlight {
                        StartHours = reader.ReadDouble(), EndHours = reader.ReadDouble(),
                        Origin = ReadVector(reader), Forward = ReadVector(reader), Up = ReadVector(reader),
                        Speed = reader.ReadSingle(), LaunchAngle = reader.ReadSingle(), Gravity = reader.ReadSingle()
                    };
                    byte hasScale = reader.ReadByte();
                    var scale = ReadVector(reader);
                    if (hasScale > 1 || (hasScale == 0 && (scale[0] != 0 || scale[1] != 0 || scale[2] != 0))) return false;
                    result.PresentationScale = hasScale == 1 ? scale : null;
                    if (!result.IsValid() || stream.Position != stream.Length) return false;
                    flight = result; return true;
                }
            }
            catch (IOException) { return false; }
        }
        private static float[] ReadVector(BinaryReader reader) =>
            new[] { reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle() };
    }
}
