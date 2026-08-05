using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CoopSpectator.Infrastructure;
using CoopSpectator.MissionBehaviors;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace CoopSpectator.UI
{
    public sealed class CoopHeroCultureSelectionVM : ViewModel
    {
        private static readonly Dictionary<string, uint> CultureColors =
            new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase)
            {
                { "empire", 0xFF6F3C8F },
                { "vlandia", 0xFFB61B23 },
                { "sturgia", 0xFF2C5D91 },
                { "aserai", 0xFFE0A529 },
                { "khuzait", 0xFF4D7D3B },
                { "battania", 0xFF315C2B }
            };

        private readonly Action<string> _advance;
        private CoopHeroCultureItemVM _currentSelectedCulture;
        private string _statusText = "Waiting for server invitation...";
        private string _progressText = string.Empty;
        private string _remainingText = string.Empty;
        private bool _canEdit;
        private DateTime? _sessionDeadlineUtc;
        private int _lastDisplayedSeconds = int.MinValue;

        public CoopHeroCultureSelectionVM(CoopHeroCreationRules rules, Action<string> advance)
        {
            _advance = advance;
            Cultures = new MBBindingList<CoopHeroCultureItemVM>();
            foreach (string cultureId in rules?.AllowedCultureIds ?? Enumerable.Empty<string>())
                Cultures.Add(CreateCultureItem(cultureId));
        }

        [DataSourceProperty] public string Title => "Culture";
        [DataSourceProperty] public string Description => "Choose your companion hero's culture:";
        [DataSourceProperty] public string SelectionText => "Hero Culture";
        [DataSourceProperty] public string NextStageText => "Next: Appearance";
        [DataSourceProperty] public MBBindingList<CoopHeroCultureItemVM> Cultures { get; }
        [DataSourceProperty] public bool AnyItemSelected => CurrentSelectedCulture != null;
        [DataSourceProperty] public bool CanAdvance => CanEdit && CurrentSelectedCulture != null;
        [DataSourceProperty] public bool IsActive => true;
        [DataSourceProperty] public bool CanEdit { get => _canEdit; private set => SetField(ref _canEdit, value, nameof(CanEdit)); }
        [DataSourceProperty] public string StatusText { get => _statusText; private set => SetField(ref _statusText, value, nameof(StatusText)); }
        [DataSourceProperty] public string ProgressText { get => _progressText; private set => SetField(ref _progressText, value, nameof(ProgressText)); }
        [DataSourceProperty] public string RemainingText { get => _remainingText; private set => SetField(ref _remainingText, value, nameof(RemainingText)); }

        [DataSourceProperty]
        public CoopHeroCultureItemVM CurrentSelectedCulture
        {
            get => _currentSelectedCulture;
            private set
            {
                if (_currentSelectedCulture == value) return;
                _currentSelectedCulture = value;
                OnPropertyChanged(nameof(CurrentSelectedCulture));
                OnPropertyChanged(nameof(AnyItemSelected));
                OnPropertyChanged(nameof(CanAdvance));
            }
        }

        public void ApplyServerEnvelope(CoopHeroCreationServerEnvelope envelope)
        {
            if (envelope == null) return;
            CanEdit = envelope.State == CoopHeroCreationParticipantState.Invited ||
                      envelope.State == CoopHeroCreationParticipantState.Editing;
            ProgressText = "Completed: " + envelope.TerminalCount + "/" + envelope.RelevantCount;
            StatusText = CoopHeroCreatorText.DescribeState(envelope.State, envelope.Reason);
            _sessionDeadlineUtc = ParseUtc(envelope.SessionDeadlineUtc);
            _lastDisplayedSeconds = int.MinValue;
            RefreshCountdown();
            OnPropertyChanged(nameof(CanAdvance));
        }

        public void Tick()
        {
            RefreshCountdown();
        }

        public void RestoreSelection(string cultureId)
        {
            if (string.IsNullOrWhiteSpace(cultureId)) return;
            CoopHeroCultureItemVM item = Cultures.FirstOrDefault(candidate =>
                string.Equals(candidate.CultureID, cultureId, StringComparison.OrdinalIgnoreCase));
            if (item != null) SelectCulture(item);
        }

        public void ExecuteNext()
        {
            if (CanAdvance) _advance?.Invoke(CurrentSelectedCulture.CultureID);
        }

        public void ExecuteDecline()
        {
            if (!CanEdit) return;
            CoopHeroCreationMissionNetwork.SendDecline();
            CanEdit = false;
            StatusText = "Decline sent. Waiting for other players...";
            OnPropertyChanged(nameof(CanAdvance));
        }

        private CoopHeroCultureItemVM CreateCultureItem(string cultureId)
        {
            uint unsignedColor;
            if (!CultureColors.TryGetValue(cultureId ?? string.Empty, out unsignedColor))
                unsignedColor = Color.White.ToUnsignedInteger();

            string name = ResolveGameText("str_culture_rich_name", cultureId, CultureFallbackName(cultureId));
            string description = ResolveGameText("str_culture_description", cultureId, name);
            return new CoopHeroCultureItemVM(
                cultureId ?? string.Empty,
                name,
                description,
                Color.FromUint(unsignedColor),
                SelectCulture);
        }

        private void SelectCulture(CoopHeroCultureItemVM selected)
        {
            if (selected == null) return;
            foreach (CoopHeroCultureItemVM item in Cultures) item.IsSelected = ReferenceEquals(item, selected);
            CurrentSelectedCulture = selected;
        }

        private void RefreshCountdown()
        {
            if (!_sessionDeadlineUtc.HasValue)
            {
                RemainingText = string.Empty;
                return;
            }

            int seconds = Math.Max(0, (int)Math.Ceiling((_sessionDeadlineUtc.Value - DateTime.UtcNow).TotalSeconds));
            if (seconds == _lastDisplayedSeconds) return;
            _lastDisplayedSeconds = seconds;
            RemainingText = "Time remaining: " + TimeSpan.FromSeconds(seconds).ToString(@"mm\:ss", CultureInfo.InvariantCulture);
        }

        private static DateTime? ParseUtc(string value)
        {
            DateTime parsed;
            return DateTime.TryParse(value, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out parsed)
                ? parsed
                : (DateTime?)null;
        }

        private static string ResolveGameText(string id, string variation, string fallback)
        {
            try
            {
                string value = GameTexts.FindText(id, variation ?? string.Empty)?.ToString();
                return string.IsNullOrWhiteSpace(value) ? fallback : value;
            }
            catch { return fallback; }
        }

        private static string CultureFallbackName(string id)
        {
            switch ((id ?? string.Empty).ToLowerInvariant())
            {
                case "empire": return "Imperials";
                case "vlandia": return "Vlandians";
                case "sturgia": return "Sturgians";
                case "aserai": return "Aserai";
                case "khuzait": return "Khuzaits";
                case "battania": return "Battanians";
                default: return id ?? string.Empty;
            }
        }
    }

    public sealed class CoopHeroCultureItemVM : ViewModel
    {
        private readonly Action<CoopHeroCultureItemVM> _select;
        private bool _isSelected;

        public CoopHeroCultureItemVM(
            string cultureId,
            string name,
            string description,
            Color cultureColor,
            Action<CoopHeroCultureItemVM> select)
        {
            CultureID = cultureId;
            NameText = name;
            ShortenedNameText = name;
            DescriptionText = description;
            CultureColor1 = cultureColor;
            _select = select;
        }

        [DataSourceProperty] public string CultureID { get; }
        [DataSourceProperty] public string NameText { get; }
        [DataSourceProperty] public string ShortenedNameText { get; }
        [DataSourceProperty] public string DescriptionText { get; }
        [DataSourceProperty] public Color CultureColor1 { get; }
        [DataSourceProperty] public bool IsSelected { get => _isSelected; set => SetField(ref _isSelected, value, nameof(IsSelected)); }
        public void ExecuteSelectCulture() => _select?.Invoke(this);
    }
}
