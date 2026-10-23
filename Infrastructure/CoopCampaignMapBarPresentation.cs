using System;
using System.Collections.Generic;
using System.Linq;

namespace CoopSpectator.Infrastructure
{
    // Presentation only. Never transport native view models, callbacks or campaign objects.
    // Session/revision admission belongs to the enclosing publication, not this value object.
    public sealed class CoopCampaignMapBarPresentation
    {
        public const int MaxItems = 32;
        public const int MaxTextLength = 4096;
        public const int MaxControlHints = 72;
        public bool IsEnabled { get; set; }
        public bool IsGatherArmyVisible { get; set; }
        public bool CanGatherArmy { get; set; }
        public bool IsCenterPanelEnabled { get; set; }
        public bool IsInfoBarEnabled { get; set; }
        public bool IsInfoBarExtended { get; set; }
        public string Date { get; set; } = string.Empty;
        public string PausedText { get; set; } = string.Empty;
        public string TutorialElementId { get; set; } = string.Empty;
        public double Time { get; set; }
        public int HoursInDay { get; set; } = 24;
        public int TimeFlowState { get; set; }
        public List<CoopCampaignMapBarInfo> PrimaryInfoItems { get; set; } = new List<CoopCampaignMapBarInfo>();
        public List<CoopCampaignMapBarInfo> SecondaryInfoItems { get; set; } = new List<CoopCampaignMapBarInfo>();
        public List<CoopCampaignMapBarNavigation> NavigationItems { get; set; } = new List<CoopCampaignMapBarNavigation>();
        public List<CoopCampaignMapBarControlTarget> ControlHints { get; set; } = new List<CoopCampaignMapBarControlTarget>();

        public bool IsValid() => Text(Date) && Text(PausedText) &&
            TutorialElementId != null && (TutorialElementId.Length == 0 || Key(TutorialElementId)) &&
            HoursInDay > 0 && HoursInDay <= 1024 &&
            !double.IsNaN(Time) && !double.IsInfinity(Time) && Time >= 0 && Time < HoursInDay &&
            TimeFlowState >= 0 && TimeFlowState <= 6 &&
            ValidInfo(PrimaryInfoItems) && ValidInfo(SecondaryInfoItems) &&
            ControlHints != null && ControlHints.Count <= MaxControlHints &&
            ControlHints.All(x => x != null && x.IsValid()) &&
            ControlHints.Select(x => x.Key).Distinct(StringComparer.Ordinal).Count() == ControlHints.Count &&
            NavigationItems != null && NavigationItems.Count <= MaxItems &&
            NavigationItems.All(x => x != null && x.IsValid()) &&
            NavigationItems.Select(x => x.ItemId).Distinct(StringComparer.Ordinal).Count() == NavigationItems.Count;

        private static bool ValidInfo(List<CoopCampaignMapBarInfo> items) => items != null &&
            items.Count <= MaxItems && items.All(x => x != null && x.IsValid());
        internal static bool Text(string text) => text != null && text.Length <= MaxTextLength && text.IndexOf('\0') < 0;
        // Native icon keys, never paths or arbitrary prefab names.
        internal static bool Key(string key) => !string.IsNullOrEmpty(key) && key.Length <= 128 &&
            key.All(c => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') ||
                (c >= '0' && c <= '9') || c == '_' || c == '-');

        public CoopCampaignMapBarPresentation Clone()
        {
            var copy = (CoopCampaignMapBarPresentation)MemberwiseClone();
            copy.PrimaryInfoItems = PrimaryInfoItems?.Select(x => x?.Clone()).ToList();
            copy.SecondaryInfoItems = SecondaryInfoItems?.Select(x => x?.Clone()).ToList();
            copy.NavigationItems = NavigationItems?.Select(x => x?.Clone()).ToList();
            copy.ControlHints = ControlHints?.Select(x => x?.Clone()).ToList();
            return copy;
        }
    }

    // Key selects a presentation binding only; host source resolution uses the opaque token.
    public sealed class CoopCampaignMapBarControlTarget
    {
        public string Key { get; set; } = string.Empty;
        public string HintToken { get; set; } = string.Empty;
        public bool IsAvailable { get; set; }
        public bool IsValid() => CoopCampaignMapBarPresentation.Key(Key) &&
            CoopCampaignMapBarHintIdentity<object>.IsToken(HintToken);
        public CoopCampaignMapBarControlTarget Clone() => (CoopCampaignMapBarControlTarget)MemberwiseClone();
    }

    public sealed class CoopCampaignMapBarInfo
    {
        public string ItemId { get; set; } = string.Empty;
        public string HintToken { get; set; } = string.Empty;
        public string VisualId { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
        public int IntValue { get; set; }
        public bool HasWarning { get; set; }
        public bool IsValid() => CoopCampaignMapBarPresentation.Key(ItemId) &&
            CoopCampaignMapBarHintIdentity<object>.IsToken(HintToken) &&
            CoopCampaignMapBarPresentation.Key(VisualId) && CoopCampaignMapBarPresentation.Text(Value);
        public CoopCampaignMapBarInfo Clone() => (CoopCampaignMapBarInfo)MemberwiseClone();
    }

    public sealed class CoopCampaignMapBarNavigation
    {
        public string ItemId { get; set; } = string.Empty;
        public string AlertText { get; set; } = string.Empty;
        public bool IsEnabled { get; set; }
        public bool IsActive { get; set; }
        public bool HasAlert { get; set; }
        public bool IsValid() => CoopCampaignMapBarPresentation.Key(ItemId) && CoopCampaignMapBarPresentation.Text(AlertText);
        public CoopCampaignMapBarNavigation Clone() => (CoopCampaignMapBarNavigation)MemberwiseClone();
    }
}
