using System;
using System.IO;
using System.Text;

namespace CoopSpectator.Infrastructure
{
    // Inner presentation sample only; entity/session/revision admission belongs
    // to the enclosing transport, not to successful decoding.
    public static class CoopCampaignMapPartyAnimationCodec
    {
        private const int Magic = 0x434D4131; // CMA1
        public const int MaximumBytes = 16 + CoopCampaignMapPartyAnimation.MaximumNameLength;
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);

        public static bool TryEncode(CoopCampaignMapPartyAnimation sample, out byte[] bytes)
        {
            bytes = null;
            if (sample == null || !sample.IsValid()) return false;
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Utf8, true))
            {
                var name = Utf8.GetBytes(sample.ActionName);
                writer.Write(Magic); writer.Write(name.Length); writer.Write(name);
                writer.Write(sample.Progress); writer.Write(sample.Speed);
                writer.Flush(); bytes = stream.ToArray(); return true;
            }
        }

        public static bool TryDecode(byte[] bytes, out CoopCampaignMapPartyAnimation sample)
        {
            sample = null;
            if (bytes == null || bytes.Length < 17 || bytes.Length > MaximumBytes) return false;
            try
            {
                using (var stream = new MemoryStream(bytes, false))
                using (var reader = new BinaryReader(stream, Utf8, true))
                {
                    if (reader.ReadInt32() != Magic) return false;
                    int length = reader.ReadInt32();
                    if (length < 1 || length > CoopCampaignMapPartyAnimation.MaximumNameLength ||
                        length != stream.Length - stream.Position - 8) return false;
                    var value = new CoopCampaignMapPartyAnimation {
                        ActionName = Utf8.GetString(reader.ReadBytes(length)),
                        Progress = reader.ReadSingle(), Speed = reader.ReadSingle() };
                    if (stream.Position != stream.Length || !value.IsValid()) return false;
                    sample = value; return true;
                }
            }
            catch (ArgumentException) { return false; }
            catch (IOException) { return false; }
        }
    }
}
