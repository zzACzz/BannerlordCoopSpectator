using System;
using CoopSpectator.Infrastructure;

internal static class SiegeTentTests
{
    internal static void Run()
    {
        var tent = new CoopCampaignMapSiegeTent { TentFrame = Frame(1.2f) };
        Check(CoopCampaignMapSiegeTent.IsValid(tent), "Native tent without clan flag");
        tent.BannerCode = "1.2.3.4.5.6.7.8.9.-10";
        Check(!CoopCampaignMapSiegeTent.IsValid(tent), "Flag requires complete local frame");
        tent.FlagFrame = Frame(0.15f);
        tent.FlagFrame[11] = 0.2f;
        Check(CoopCampaignMapSiegeTent.IsValid(tent), "Tent and flag tuple admitted");
        var copy = tent.Clone();
        copy.TentFrame[0] = 2;
        copy.FlagFrame[11] = 7;
        Check(tent.TentFrame[0] == 1.2f && tent.FlagFrame[11] == 0.2f, "Clone owns both frames");
        foreach (string banner in new[] { null, "", "1.2", "1.2.3.4.5.6.7.8.9.+10", "1.2.3.4.5.6.7.8.9.2147483648", new string('1', 513) })
        {
            copy = tent.Clone(); copy.BannerCode = banner;
            Check(!CoopCampaignMapSiegeTent.IsValid(copy), "Malformed or orphan flag rejected");
        }
        foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, 100001f })
        for (int i = 0; i < 12; i++)
        {
            copy = tent.Clone(); copy.TentFrame[i] = invalid;
            Check(!CoopCampaignMapSiegeTent.IsValid(copy), "Invalid tent transform rejected");
            copy = tent.Clone(); copy.FlagFrame[i] = invalid;
            Check(!CoopCampaignMapSiegeTent.IsValid(copy), "Invalid flag transform rejected");
        }
        foreach (float[] malformed in new[] { null, new float[11], new float[12], new float[13] })
        {
            copy = tent.Clone(); copy.TentFrame = malformed;
            Check(!CoopCampaignMapSiegeTent.IsValid(copy), "Missing, singular or wrong-size frame rejected");
        }
        Check(!CoopCampaignMapSiegeTent.IsValid(null), "Missing presentation rejected");
        foreach (var source in new[] { tent, new CoopCampaignMapSiegeTent { TentFrame = Frame(1.2f) } })
        {
            Check(CoopCampaignMapSiegeTentCodec.TryEncode(source, out var bytes), "Encode valid tent");
            Check(CoopCampaignMapSiegeTentCodec.TryDecode(bytes, out var decoded) &&
                decoded.BannerCode == source.BannerCode && decoded.TentFrame[0] == 1.2f &&
                (source.FlagFrame == null ? decoded.FlagFrame == null : decoded.FlagFrame[11] == 0.2f),
                "Round trip preserves visible presentation");
            for (int length = 0; length < bytes.Length; length++)
            {
                var prefix = new byte[length]; Array.Copy(bytes, prefix, length);
                Check(!CoopCampaignMapSiegeTentCodec.TryDecode(prefix, out _), "Every truncation rejected");
            }
            var trailing = new byte[bytes.Length + 1]; Array.Copy(bytes, trailing, bytes.Length);
            Check(!CoopCampaignMapSiegeTentCodec.TryDecode(trailing, out _), "Trailing bytes rejected");
            foreach (int length in new[] { -1, int.MaxValue, 513 })
            {
                var corrupt = (byte[])bytes.Clone(); Array.Copy(BitConverter.GetBytes(length), 0, corrupt, 52, 4);
                Check(!CoopCampaignMapSiegeTentCodec.TryDecode(corrupt, out _), "Length validated before allocation");
            }
            var badFrame = (byte[])bytes.Clone(); Array.Copy(BitConverter.GetBytes(float.NaN), 0, badFrame, 4, 4);
            Check(!CoopCampaignMapSiegeTentCodec.TryDecode(badFrame, out _), "Wire NaN rejected");
            var badMagic = (byte[])bytes.Clone(); badMagic[0] ^= 1;
            Check(!CoopCampaignMapSiegeTentCodec.TryDecode(badMagic, out _), "Unknown schema rejected");
            if (source.FlagFrame != null)
            {
                var badUtf8 = (byte[])bytes.Clone(); badUtf8[56] = 255;
                Check(!CoopCampaignMapSiegeTentCodec.TryDecode(badUtf8, out _), "Malformed UTF8 rejected");
            }
        }
        Check(!CoopCampaignMapSiegeTentCodec.TryDecode(null, out _), "Missing frame rejected");
        Check(!CoopCampaignMapSiegeTentCodec.TryEncode(null, out _), "Missing value rejected");
    }

    private static float[] Frame(float scale) => new[] { scale, 0f, 0f, 0f, scale, 0f, 0f, 0f, scale, 0f, 0f, 0f };
    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
