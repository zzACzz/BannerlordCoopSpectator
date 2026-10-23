using System;
using System.IO;
using System.Text;

namespace CoopSpectator.Infrastructure
{
    // Inner presentation only. Caller must bind it to a disclosed catalog entity.
    public static class CoopCampaignMapSiegeTentCodec
    {
        private const int Magic = 0x43535431; // CST1
        public const int MinimumBytes = 56;
        public const int MaximumBytes = 104 + CoopCampaignMapPrototypeContract.MaxBannerCodeCharacters;
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);

        public const int MaximumEncodedCharacters = ((MaximumBytes + 2) / 3) * 4;

        public static bool TryDecodeText(string encoded, out CoopCampaignMapSiegeTent value)
        {
            value = null;
            if (encoded == string.Empty) return true;
            if (encoded == null || encoded.Length > MaximumEncodedCharacters) return false;
            try
            {
                byte[] bytes = Convert.FromBase64String(encoded);
                return Convert.ToBase64String(bytes) == encoded && TryDecode(bytes, out value);
            }
            catch (FormatException) { return false; }
        }

        public static bool IsValidForEntity(string encoded, CoopCampaignMapPrototypeEntityKind kind,
            CoopCampaignMapPrototypePartyVisualKind visualKind, bool hasActors)
        {
            if (!TryDecodeText(encoded, out var value)) return false;
            bool tent = visualKind == CoopCampaignMapPrototypePartyVisualKind.SiegeTent;
            return tent ? kind != CoopCampaignMapPrototypeEntityKind.Settlement && value != null && !hasActors
                : value == null;
        }

        public static bool TryEncode(CoopCampaignMapSiegeTent value, out byte[] bytes)
        {
            bytes = null;
            if (!CoopCampaignMapSiegeTent.IsValid(value)) return false;
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Utf8, true))
            {
                writer.Write(Magic);
                foreach (float component in value.TentFrame) writer.Write(component);
                byte[] banner = Utf8.GetBytes(value.BannerCode);
                writer.Write(banner.Length);
                writer.Write(banner);
                if (banner.Length != 0)
                    foreach (float component in value.FlagFrame) writer.Write(component);
                writer.Flush();
                bytes = stream.ToArray();
                return true;
            }
        }

        public static bool TryDecode(byte[] bytes, out CoopCampaignMapSiegeTent value)
        {
            value = null;
            if (bytes == null || bytes.Length < MinimumBytes || bytes.Length > MaximumBytes) return false;
            try
            {
                using (var stream = new MemoryStream(bytes, false))
                using (var reader = new BinaryReader(stream, Utf8, true))
                {
                    if (reader.ReadInt32() != Magic) return false;
                    var result = new CoopCampaignMapSiegeTent { TentFrame = ReadFrame(reader) };
                    int length = reader.ReadInt32();
                    if (length < 0 || length > CoopCampaignMapPrototypeContract.MaxBannerCodeCharacters ||
                        stream.Length - stream.Position != length + (length == 0 ? 0 : 48)) return false;
                    result.BannerCode = Utf8.GetString(reader.ReadBytes(length));
                    if (length != 0) result.FlagFrame = ReadFrame(reader);
                    if (!CoopCampaignMapSiegeTent.IsValid(result)) return false;
                    value = result;
                    return true;
                }
            }
            catch (ArgumentException) { return false; }
            catch (IOException) { return false; }
        }

        private static float[] ReadFrame(BinaryReader reader)
        {
            var result = new float[12];
            for (int i = 0; i < result.Length; i++) result[i] = reader.ReadSingle();
            return result;
        }
    }
}
