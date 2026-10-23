using System;
using System.Collections.Generic;
using System.Linq;

namespace CoopSpectator.Infrastructure
{
    // Native RundownTooltip presentation, not a client-side finance calculation.
    // Correlation/admission belongs to the enclosing hint response.
    public sealed class CoopCampaignMapRundownPresentation
    {
        public const int MaxLines = 512;
        public bool IsActive { get; set; }
        public bool IsExtended { get; set; }
        public int ValueCategorizationAsInt { get; set; }
        public string TitleText { get; set; } = string.Empty;
        public string ExpectedChangeText { get; set; } = string.Empty;
        public string ExtendText { get; set; } = string.Empty;
        public float CurrentExpectedChange { get; set; }
        public List<CoopCampaignMapRundownLine> Lines { get; set; } = new List<CoopCampaignMapRundownLine>();

        public bool IsValid() => ValueCategorizationAsInt >= 0 && ValueCategorizationAsInt <= 2 &&
            CoopCampaignMapBarPresentation.Text(TitleText) && CoopCampaignMapBarPresentation.Text(ExpectedChangeText) &&
            CoopCampaignMapBarPresentation.Text(ExtendText) && Finite(CurrentExpectedChange) &&
            Lines != null && Lines.Count <= MaxLines && Lines.All(x => x != null && x.IsValid());

        internal static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        public CoopCampaignMapRundownPresentation Clone()
        {
            var copy = (CoopCampaignMapRundownPresentation)MemberwiseClone();
            copy.Lines = Lines?.Select(x => x?.Clone()).ToList();
            return copy;
        }
    }

    public sealed class CoopCampaignMapRundownLine
    {
        public string Name { get; set; } = string.Empty;
        public string ValueAsString { get; set; } = string.Empty;
        public float Value { get; set; }
        public bool IsValid() => CoopCampaignMapBarPresentation.Text(Name) &&
            CoopCampaignMapBarPresentation.Text(ValueAsString) && CoopCampaignMapRundownPresentation.Finite(Value);
        public CoopCampaignMapRundownLine Clone() => (CoopCampaignMapRundownLine)MemberwiseClone();
    }
}
