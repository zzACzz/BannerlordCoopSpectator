using System;
using System.Collections.Generic;
using System.Linq;

namespace CoopSpectator.Infrastructure
{
    // Detached host presentation. Not authorization and not yet a wire contract.
    public sealed class CoopCampaignMapControlHint
    {
        public const int MaxRows = 32;
        public const int MaxTextCharacters = 4096;
        public string Text { get; set; } = string.Empty;
        public List<CoopCampaignMapTooltipRow> Rows { get; set; } = new List<CoopCampaignMapTooltipRow>();

        // Exactly one presentation shape; absence is represented by null outside this type.
        public bool IsValid()
        {
            if (!ValidText(Text) || Rows == null || Rows.Count > MaxRows) return false;
            if (Text.Length > 0) return Rows.Count == 0;
            if (Rows.Count == 0) return false;
            return Rows.All(row => row != null && ValidText(row.Definition) && ValidText(row.Value) &&
                row.TextHeight >= -1 && row.TextHeight <= 4096 &&
                row.PropertyModifier >= 0 && row.PropertyModifier <= CoopCampaignMapTooltipPayloadCodec.MaxPropertyModifier &&
                Channel(row.Red) && Channel(row.Green) && Channel(row.Blue) && Channel(row.Alpha));
        }

        public CoopCampaignMapControlHint Clone()
        {
            return new CoopCampaignMapControlHint
            {
                Text = Text,
                Rows = Rows?.Select(row => row == null ? null : new CoopCampaignMapTooltipRow
                {
                    Definition = row.Definition, Value = row.Value, TextHeight = row.TextHeight,
                    PropertyModifier = row.PropertyModifier, Red = row.Red, Green = row.Green,
                    Blue = row.Blue, Alpha = row.Alpha
                }).ToList()
            };
        }

        private static bool ValidText(string text)
        {
            if (text == null || text.Length > MaxTextCharacters) return false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (char.IsControl(c) && c != '\r' && c != '\n' && c != '\t') return false;
                if (char.IsHighSurrogate(c))
                {
                    if (++i >= text.Length || !char.IsLowSurrogate(text[i])) return false;
                }
                else if (char.IsLowSurrogate(c)) return false;
            }
            return true;
        }

        private static bool Channel(float value) => !float.IsNaN(value) &&
            !float.IsInfinity(value) && value >= 0f && value <= 1f;
    }
}
