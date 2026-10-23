using System;
using System.Globalization;

namespace CoopSpectator.Infrastructure
{
    // Presentation only: local frames copied from the host's prepared tent and flag.
    // No strength, army ownership, campaign objects or arbitrary mesh names cross here.
    public sealed class CoopCampaignMapSiegeTent
    {
        public const string TentMesh = "map_icon_siege_camp_tent";
        public const string FlagMesh = "campaign_flag";
        public float[] TentFrame { get; set; }
        public string BannerCode { get; set; } = string.Empty;
        public float[] FlagFrame { get; set; }

        public CoopCampaignMapSiegeTent Clone() => new CoopCampaignMapSiegeTent
        {
            TentFrame = (float[])TentFrame?.Clone(),
            BannerCode = BannerCode,
            FlagFrame = (float[])FlagFrame?.Clone()
        };

        public static bool IsValid(CoopCampaignMapSiegeTent value)
        {
            if (value == null || !ValidFrame(value.TentFrame) || value.BannerCode == null ||
                value.BannerCode.Length > CoopCampaignMapPrototypeContract.MaxBannerCodeCharacters)
                return false;
            if (value.BannerCode.Length == 0) return value.FlagFrame == null;
            if (!ValidFrame(value.FlagFrame)) return false;
            string[] fields = value.BannerCode.Split('.');
            if (fields.Length % 10 != 0) return false;
            foreach (string field in fields)
            {
                if (field.Length == 0) return false;
                for (int i = 0; i < field.Length; i++)
                    if ((field[i] < '0' || field[i] > '9') && !(i == 0 && field[i] == '-'))
                        return false;
                if (!int.TryParse(field, NumberStyles.AllowLeadingSign,
                    CultureInfo.InvariantCulture, out _)) return false;
            }
            return true;
        }

        private static bool ValidFrame(float[] frame)
        {
            if (frame == null || frame.Length != 12) return false;
            // Same broad finite transform envelope as map decals; reject, never clamp.
            for (int i = 0; i < frame.Length; i++)
                if (float.IsNaN(frame[i]) || float.IsInfinity(frame[i]) ||
                    Math.Abs(frame[i]) > (i < 9 ? 64f : 100000f)) return false;
            double determinant = (double)frame[0] * ((double)frame[4] * frame[8] - (double)frame[5] * frame[7]) -
                (double)frame[1] * ((double)frame[3] * frame[8] - (double)frame[5] * frame[6]) +
                (double)frame[2] * ((double)frame[3] * frame[7] - (double)frame[4] * frame[6]);
            return Math.Abs(determinant) > 0.000001d;
        }
    }
}
