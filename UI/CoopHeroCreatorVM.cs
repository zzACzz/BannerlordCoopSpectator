using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CoopSpectator.Infrastructure;
using CoopSpectator.MissionBehaviors;
using TaleWorlds.Library;

namespace CoopSpectator.UI
{
    public sealed class CoopHeroCreatorVM : ViewModel
    {
        private readonly Action _openAppearance;
        private readonly Action _backToCulture;
        private CoopHeroCreationRules _rules;
        private string _name = "Coop Hero";
        private string _cultureId = string.Empty;
        private int _age = 20;
        private bool _isFemale;
        private string _bodyProperties = string.Empty;
        private string _statusText = "Waiting for server invitation...";
        private string _progressText = string.Empty;
        private string _remainingText = string.Empty;
        private bool _canEdit;
        private bool _canSubmit;
        private int _revision;
        private DateTime? _sessionDeadlineUtc;
        private int _lastDisplayedSeconds = int.MinValue;
        private MBBindingList<CoopHeroAttributeItemVM> _attributes = new MBBindingList<CoopHeroAttributeItemVM>();
        private MBBindingList<CoopHeroSkillItemVM> _skills = new MBBindingList<CoopHeroSkillItemVM>();

        public CoopHeroCreatorVM(Action openAppearance, Action backToCulture)
        {
            _openAppearance = openAppearance;
            _backToCulture = backToCulture;
        }

        [DataSourceProperty] public string TitleText => "Companion Hero Creation";
        [DataSourceProperty] public string SubtitleText => "Allocate attributes. Culture, gender, and age are taken from the preceding native screens.";
        [DataSourceProperty] public string StatusText { get => _statusText; private set => SetField(ref _statusText, value, nameof(StatusText)); }
        [DataSourceProperty] public string ProgressText { get => _progressText; private set => SetField(ref _progressText, value, nameof(ProgressText)); }
        [DataSourceProperty] public string RemainingText { get => _remainingText; private set => SetField(ref _remainingText, value, nameof(RemainingText)); }
        [DataSourceProperty] public bool CanEdit { get => _canEdit; private set => SetField(ref _canEdit, value, nameof(CanEdit)); }
        [DataSourceProperty] public bool CanSubmit { get => _canSubmit; private set => SetField(ref _canSubmit, value, nameof(CanSubmit)); }
        [DataSourceProperty] public MBBindingList<CoopHeroAttributeItemVM> Attributes { get => _attributes; private set => SetField(ref _attributes, value, nameof(Attributes)); }
        [DataSourceProperty] public MBBindingList<CoopHeroSkillItemVM> Skills { get => _skills; private set => SetField(ref _skills, value, nameof(Skills)); }

        [DataSourceProperty]
        public string HeroName
        {
            get => _name;
            set
            {
                string normalized = value ?? string.Empty;
                if (_name == normalized) return;
                _name = normalized;
                OnPropertyChanged(nameof(HeroName));
                RefreshAvailability();
            }
        }

        [DataSourceProperty] public string CultureText => "Culture: " + DisplayCulture(_cultureId);
        [DataSourceProperty] public string GenderText => "Gender: " + (_isFemale ? "female" : "male");
        [DataSourceProperty] public string AgeText => "Age: " + _age;
        [DataSourceProperty] public string AppearanceText => string.IsNullOrWhiteSpace(_bodyProperties) ? "Appearance not saved" : "Appearance saved";
        [DataSourceProperty] public string AttributeBudgetText => "Attributes: " + Attributes.Sum(item => item.Value) + "/" + CoopHeroCreationRules.GetAttributeBudget(_age);
        [DataSourceProperty] public string FocusBudgetText => "Focus: " + Skills.Sum(item => item.Focus) + "/" + CoopHeroCreationRules.GetFocusBudget(_age);
        [DataSourceProperty] public string SkillBudgetText => "Skills: " + Skills.Sum(item => item.SkillValue) + "/100";

        public void Configure(CoopHeroCreationRules rules, string cultureId, string bodyProperties, bool isFemale, int age)
        {
            if (rules == null) return;
            if (_rules == null)
            {
                _rules = rules;
                InitializeBudgets();
            }

            SetCulture(cultureId);
            SetBodyProperties(bodyProperties, isFemale, age);
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
            RefreshAvailability();
        }

        public void Tick()
        {
            RefreshCountdown();
        }

        public void SetCulture(string cultureId)
        {
            if (string.Equals(_cultureId, cultureId, StringComparison.OrdinalIgnoreCase)) return;
            _cultureId = cultureId ?? string.Empty;
            OnPropertyChanged(nameof(CultureText));
            RefreshAvailability();
        }

        public void SetBodyProperties(string bodyProperties, bool isFemale, int age)
        {
            _bodyProperties = bodyProperties ?? string.Empty;
            _isFemale = isFemale;
            int normalizedAge = Math.Max(20, Math.Min(50, age));
            if (_age != normalizedAge)
            {
                _age = normalizedAge;
                if (Attributes.Count > 0) NormalizeTotal(Attributes, CoopHeroCreationRules.GetAttributeBudget(_age));
                if (Skills.Count > 0) NormalizeFocus(CoopHeroCreationRules.GetFocusBudget(_age));
            }
            OnPropertyChanged(nameof(AppearanceText));
            OnPropertyChanged(nameof(GenderText));
            OnPropertyChanged(nameof(AgeText));
            RefreshBudgets();
        }

        public void ExecuteOpenAppearance()
        {
            if (CanEdit) _openAppearance?.Invoke();
        }

        public void ExecuteBackToCulture()
        {
            if (CanEdit) _backToCulture?.Invoke();
        }

        public void ExecuteSubmit()
        {
            if (!CanSubmit || _rules == null) return;
            CoopHeroDraft draft = BuildDraft();
            if (!CoopHeroCreationContract.ValidateDraft(draft, _rules, out string error))
            {
                StatusText = "Validation failed: " + error;
                return;
            }

            _revision++;
            if (!CoopHeroCreationMissionNetwork.SendSubmit(draft, _revision, Guid.NewGuid().ToString("N")))
            {
                StatusText = "Failed to submit the hero to the server.";
                return;
            }

            CanSubmit = false;
            StatusText = "Hero submitted. Waiting for server confirmation...";
        }

        private void InitializeBudgets()
        {
            Attributes = new MBBindingList<CoopHeroAttributeItemVM>();
            foreach (string id in _rules.AttributeIds)
                Attributes.Add(new CoopHeroAttributeItemVM(id, 3, ChangeAttribute));

            Skills = new MBBindingList<CoopHeroSkillItemVM>();
            for (int index = 0; index < _rules.SkillIds.Count; index++)
            {
                int skill = index < 10 ? 10 : 0;
                int focus = index < 2 ? 2 : index < 10 ? 1 : 0;
                Skills.Add(new CoopHeroSkillItemVM(_rules.SkillIds[index], focus, skill, ChangeFocus, ChangeSkill));
            }
            RefreshBudgets();
        }

        private void ChangeAttribute(CoopHeroAttributeItemVM item, int delta)
        {
            if (!CanEdit || item == null) return;
            int total = Attributes.Sum(candidate => candidate.Value);
            int budget = CoopHeroCreationRules.GetAttributeBudget(_age);
            if (delta > 0 && item.Value < 10 && total < budget) item.Value++;
            if (delta < 0 && item.Value > 2) item.Value--;
            RefreshBudgets();
        }

        private void ChangeFocus(CoopHeroSkillItemVM item, int delta)
        {
            if (!CanEdit || item == null) return;
            int total = Skills.Sum(candidate => candidate.Focus);
            int budget = CoopHeroCreationRules.GetFocusBudget(_age);
            int required = item.SkillValue / 10;
            if (delta > 0 && item.Focus < 5 && total < budget) item.Focus++;
            if (delta < 0 && item.Focus > required) item.Focus--;
            RefreshBudgets();
        }

        private void ChangeSkill(CoopHeroSkillItemVM item, int delta)
        {
            if (!CanEdit || item == null) return;
            int total = Skills.Sum(candidate => candidate.SkillValue);
            if (delta > 0 && item.SkillValue < 50 && total < 100 && item.Focus >= item.SkillValue / 10 + 1)
                item.SkillValue += 10;
            if (delta < 0 && item.SkillValue > 0) item.SkillValue -= 10;
            RefreshBudgets();
        }

        private CoopHeroDraft BuildDraft()
        {
            return new CoopHeroDraft
            {
                Name = (_name ?? string.Empty).Trim(),
                CultureId = _cultureId,
                Age = _age,
                IsFemale = _isFemale,
                BodyProperties = _bodyProperties,
                Attributes = Attributes.ToDictionary(item => item.AttributeId, item => item.Value, StringComparer.Ordinal),
                Focus = Skills.ToDictionary(item => item.SkillId, item => item.Focus, StringComparer.Ordinal),
                Skills = Skills.ToDictionary(item => item.SkillId, item => item.SkillValue, StringComparer.Ordinal),
                PerkIds = new List<string>(),
                TraitLevels = new Dictionary<string, int>(StringComparer.Ordinal)
            };
        }

        private void RefreshAvailability()
        {
            RefreshControlAvailability();
            CanSubmit = CanEdit && _rules != null &&
                        !string.IsNullOrWhiteSpace(_cultureId) &&
                        !string.IsNullOrWhiteSpace(_bodyProperties) &&
                        (_name ?? string.Empty).Trim().Length >= 2 &&
                        Attributes.Sum(item => item.Value) == CoopHeroCreationRules.GetAttributeBudget(_age) &&
                        Skills.Sum(item => item.Focus) == CoopHeroCreationRules.GetFocusBudget(_age) &&
                        Skills.Sum(item => item.SkillValue) == 100;
        }

        private void RefreshControlAvailability()
        {
            int attributeBudget = CoopHeroCreationRules.GetAttributeBudget(_age);
            int attributeTotal = Attributes.Sum(item => item.Value);
            foreach (CoopHeroAttributeItemVM item in Attributes)
            {
                item.SetAvailability(
                    CanEdit && item.Value > 2,
                    CanEdit && item.Value < 10 && attributeTotal < attributeBudget);
            }

            int focusBudget = CoopHeroCreationRules.GetFocusBudget(_age);
            int focusTotal = Skills.Sum(item => item.Focus);
            int skillTotal = Skills.Sum(item => item.SkillValue);
            foreach (CoopHeroSkillItemVM item in Skills)
            {
                int requiredFocus = item.SkillValue / 10;
                item.SetAvailability(
                    CanEdit && item.Focus > requiredFocus,
                    CanEdit && item.Focus < 5 && focusTotal < focusBudget,
                    CanEdit && item.SkillValue > 0,
                    CanEdit && item.SkillValue < 50 && skillTotal < 100 && item.Focus >= requiredFocus + 1);
            }
        }

        private void RefreshBudgets()
        {
            OnPropertyChanged(nameof(AttributeBudgetText));
            OnPropertyChanged(nameof(FocusBudgetText));
            OnPropertyChanged(nameof(SkillBudgetText));
            RefreshAvailability();
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

        private static void NormalizeTotal(MBBindingList<CoopHeroAttributeItemVM> items, int target)
        {
            while (items.Sum(item => item.Value) < target)
            {
                CoopHeroAttributeItemVM item = items.FirstOrDefault(candidate => candidate.Value < 10);
                if (item == null) break;
                item.Value++;
            }
            while (items.Sum(item => item.Value) > target)
            {
                CoopHeroAttributeItemVM item = items.Reverse().FirstOrDefault(candidate => candidate.Value > 2);
                if (item == null) break;
                item.Value--;
            }
        }

        private void NormalizeFocus(int target)
        {
            while (Skills.Sum(item => item.Focus) < target)
            {
                CoopHeroSkillItemVM item = Skills.FirstOrDefault(candidate => candidate.Focus < 5);
                if (item == null) break;
                item.Focus++;
            }
            while (Skills.Sum(item => item.Focus) > target)
            {
                CoopHeroSkillItemVM item = Skills.Reverse().FirstOrDefault(candidate => candidate.Focus > candidate.SkillValue / 10);
                if (item == null) break;
                item.Focus--;
            }
        }

        private static string DisplayCulture(string cultureId)
        {
            switch ((cultureId ?? string.Empty).ToLowerInvariant())
            {
                case "empire": return "Empire";
                case "vlandia": return "Vlandia";
                case "sturgia": return "Sturgia";
                case "aserai": return "Aserai";
                case "khuzait": return "Khuzait";
                case "battania": return "Battania";
                default: return string.IsNullOrWhiteSpace(cultureId) ? "—" : cultureId;
            }
        }
    }

    public sealed class CoopHeroAttributeItemVM : ViewModel
    {
        private readonly Action<CoopHeroAttributeItemVM, int> _change;
        private int _value;
        private bool _canDecrease;
        private bool _canIncrease;

        public CoopHeroAttributeItemVM(string id, int value, Action<CoopHeroAttributeItemVM, int> change)
        {
            AttributeId = id;
            _value = value;
            _change = change;
        }

        [DataSourceProperty] public string AttributeId { get; }
        [DataSourceProperty] public string NameText => AttributeId;
        [DataSourceProperty] public string ValueText => _value.ToString(CultureInfo.InvariantCulture);
        [DataSourceProperty] public int Value { get => _value; set { if (_value == value) return; _value = value; OnPropertyChanged(nameof(Value)); OnPropertyChanged(nameof(ValueText)); } }
        [DataSourceProperty] public bool CanDecrease { get => _canDecrease; private set => SetField(ref _canDecrease, value, nameof(CanDecrease)); }
        [DataSourceProperty] public bool CanIncrease { get => _canIncrease; private set => SetField(ref _canIncrease, value, nameof(CanIncrease)); }
        public void SetAvailability(bool canDecrease, bool canIncrease) { CanDecrease = canDecrease; CanIncrease = canIncrease; }
        public void ExecuteMinus() => _change?.Invoke(this, -1);
        public void ExecutePlus() => _change?.Invoke(this, 1);
    }

    public sealed class CoopHeroSkillItemVM : ViewModel
    {
        private readonly Action<CoopHeroSkillItemVM, int> _changeFocus;
        private readonly Action<CoopHeroSkillItemVM, int> _changeSkill;
        private int _focus;
        private int _skillValue;
        private bool _canDecreaseFocus;
        private bool _canIncreaseFocus;
        private bool _canDecreaseSkill;
        private bool _canIncreaseSkill;

        public CoopHeroSkillItemVM(string id, int focus, int skillValue, Action<CoopHeroSkillItemVM, int> changeFocus, Action<CoopHeroSkillItemVM, int> changeSkill)
        {
            SkillId = id;
            _focus = focus;
            _skillValue = skillValue;
            _changeFocus = changeFocus;
            _changeSkill = changeSkill;
        }

        [DataSourceProperty] public string SkillId { get; }
        [DataSourceProperty] public string NameText => SkillId;
        [DataSourceProperty] public string FocusText => _focus.ToString(CultureInfo.InvariantCulture);
        [DataSourceProperty] public string SkillValueText => _skillValue.ToString(CultureInfo.InvariantCulture);
        [DataSourceProperty] public int Focus { get => _focus; set { if (_focus == value) return; _focus = value; OnPropertyChanged(nameof(Focus)); OnPropertyChanged(nameof(FocusText)); } }
        [DataSourceProperty] public int SkillValue { get => _skillValue; set { if (_skillValue == value) return; _skillValue = value; OnPropertyChanged(nameof(SkillValue)); OnPropertyChanged(nameof(SkillValueText)); } }
        [DataSourceProperty] public bool CanDecreaseFocus { get => _canDecreaseFocus; private set => SetField(ref _canDecreaseFocus, value, nameof(CanDecreaseFocus)); }
        [DataSourceProperty] public bool CanIncreaseFocus { get => _canIncreaseFocus; private set => SetField(ref _canIncreaseFocus, value, nameof(CanIncreaseFocus)); }
        [DataSourceProperty] public bool CanDecreaseSkill { get => _canDecreaseSkill; private set => SetField(ref _canDecreaseSkill, value, nameof(CanDecreaseSkill)); }
        [DataSourceProperty] public bool CanIncreaseSkill { get => _canIncreaseSkill; private set => SetField(ref _canIncreaseSkill, value, nameof(CanIncreaseSkill)); }
        public void SetAvailability(bool canDecreaseFocus, bool canIncreaseFocus, bool canDecreaseSkill, bool canIncreaseSkill)
        {
            CanDecreaseFocus = canDecreaseFocus;
            CanIncreaseFocus = canIncreaseFocus;
            CanDecreaseSkill = canDecreaseSkill;
            CanIncreaseSkill = canIncreaseSkill;
        }
        public void ExecuteFocusMinus() => _changeFocus?.Invoke(this, -1);
        public void ExecuteFocusPlus() => _changeFocus?.Invoke(this, 1);
        public void ExecuteSkillMinus() => _changeSkill?.Invoke(this, -1);
        public void ExecuteSkillPlus() => _changeSkill?.Invoke(this, 1);
    }

    internal static class CoopHeroCreatorText
    {
        public static string DescribeState(CoopHeroCreationParticipantState state, string reason)
        {
            switch (state)
            {
                case CoopHeroCreationParticipantState.Invited: return "Invitation received.";
                case CoopHeroCreationParticipantState.Editing: return "Editing hero.";
                case CoopHeroCreationParticipantState.Completed: return "Hero accepted by the server. Waiting for other players...";
                case CoopHeroCreationParticipantState.Declined: return "You declined. Waiting for other players...";
                case CoopHeroCreationParticipantState.AlreadyExists: return "A hero already exists for this player.";
                case CoopHeroCreationParticipantState.Late: return "Participant registration has already closed.";
                case CoopHeroCreationParticipantState.IdentityUnavailable: return "The server did not receive a stable player identifier.";
                case CoopHeroCreationParticipantState.TimedOut: return "Hero creation timed out.";
                case CoopHeroCreationParticipantState.Disconnected: return "Connection lost; reconnect grace period is active.";
                default: return string.IsNullOrWhiteSpace(reason) ? state.ToString() : reason;
            }
        }
    }
}
