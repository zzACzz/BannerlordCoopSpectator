using System;
using CoopSpectator.Infrastructure.Hideout;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace CoopSpectator.UI
{
    public sealed class CoopHideoutBossConversationVM : ViewModel
    {
        private readonly Action<CoopHideoutBossChoice> _submitChoice;
        private bool _selectionSubmitted;

        public CoopHideoutBossConversationVM(
            string bossName,
            bool choicesEnabled,
            Action<CoopHideoutBossChoice> submitChoice)
        {
            _submitChoice = submitChoice;
            CurrentCharacterNameLbl = string.IsNullOrWhiteSpace(bossName)
                ? new TextObject("{=3P1a6bA7}Bandit Leader").ToString()
                : bossName;
            DialogText = new TextObject(
                "{=nYCXzAYH}You! You've cut quite a swathe through my men there, damn you. How about we settle this, one-on-one?").ToString();
            GoldText = ResolveLocalPlayerGoldText();
            GoldHint = new CoopHideoutBossConversationHintVM(
                new TextObject("{=o5G8A8ZH}Your Denars"));
            FactionHint = new CoopHideoutBossConversationHintVM();
            AnswerList = new MBBindingList<CoopHideoutBossConversationOptionVM>();
            AttackerParties = new MBBindingList<CoopHideoutBossConversationOptionVM>();
            DefenderParties = new MBBindingList<CoopHideoutBossConversationOptionVM>();

            string disabledHint = choicesEnabled
                ? string.Empty
                : "The campaign host is choosing the response.";
            AnswerList.Add(new CoopHideoutBossConversationOptionVM(
                new TextObject("{=dzXaXKaC}Very well.").ToString(),
                choicesEnabled,
                disabledHint,
                () => SubmitChoice(CoopHideoutBossChoice.Duel)));
            AnswerList.Add(new CoopHideoutBossConversationOptionVM(
                new TextObject("{=ukRZd2AA}I don't fight duels with brigands.").ToString(),
                choicesEnabled,
                disabledHint,
                () => SubmitChoice(CoopHideoutBossChoice.AllBattle)));
        }

        [DataSourceProperty]
        public string CurrentCharacterNameLbl { get; }

        [DataSourceProperty]
        public string DialogText { get; }

        [DataSourceProperty]
        public string GoldText { get; }

        [DataSourceProperty]
        public CoopHideoutBossConversationHintVM GoldHint { get; }

        [DataSourceProperty]
        public CoopHideoutBossConversationHintVM FactionHint { get; }

        [DataSourceProperty]
        public MBBindingList<CoopHideoutBossConversationOptionVM> AnswerList { get; }

        [DataSourceProperty]
        public MBBindingList<CoopHideoutBossConversationOptionVM> AttackerParties { get; }

        [DataSourceProperty]
        public MBBindingList<CoopHideoutBossConversationOptionVM> DefenderParties { get; }

        [DataSourceProperty]
        public ViewModel AttackerLeader => null;

        [DataSourceProperty]
        public ViewModel DefenderLeader => null;

        [DataSourceProperty]
        public ViewModel PowerComparer => null;

        [DataSourceProperty]
        public ViewModel Persuasion => null;

        [DataSourceProperty]
        public ViewModel ConversedHeroBanner => null;

        [DataSourceProperty]
        public bool IsAggressive => false;

        [DataSourceProperty]
        public bool IsPersuading => false;

        [DataSourceProperty]
        public bool IsBannerEnabled => false;

        [DataSourceProperty]
        public bool IsCurrentCharacterValidInEncyclopedia => false;

        [DataSourceProperty]
        public string ContinueText => string.Empty;

        [DataSourceProperty]
        public string PersuasionText => string.Empty;

        public void ExecuteFinalizeSelection()
        {
        }

        public void ExecuteContinue()
        {
        }

        public void ExecuteLink(string link)
        {
        }

        public void ExecuteConversedHeroLink()
        {
        }

        public void ExecuteHeroTooltip()
        {
        }

        public void ExecuteCloseTooltip()
        {
        }

        public override void OnFinalize()
        {
            foreach (CoopHideoutBossConversationOptionVM option in AnswerList)
                option?.OnFinalize();
            AnswerList.Clear();
            base.OnFinalize();
        }

        private void SubmitChoice(CoopHideoutBossChoice choice)
        {
            if (_selectionSubmitted)
                return;

            _selectionSubmitted = true;
            foreach (CoopHideoutBossConversationOptionVM option in AnswerList)
                option?.DisableAfterSubmission();
            _submitChoice?.Invoke(choice);
        }

        private static string ResolveLocalPlayerGoldText()
        {
            try
            {
                return Hero.MainHero?.Gold.ToString() ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }
    }

    public sealed class CoopHideoutBossConversationOptionVM : ViewModel
    {
        private readonly Action _execute;
        private bool _isEnabled;

        public CoopHideoutBossConversationOptionVM(
            string itemText,
            bool isEnabled,
            string disabledHint,
            Action execute)
        {
            ItemText = itemText ?? string.Empty;
            _isEnabled = isEnabled;
            _execute = execute;
            OptionHint = string.IsNullOrWhiteSpace(disabledHint)
                ? new CoopHideoutBossConversationHintVM()
                : new CoopHideoutBossConversationHintVM(
                    new TextObject(disabledHint));
        }

        [DataSourceProperty]
        public string ItemText { get; }

        [DataSourceProperty]
        public bool IsEnabled
        {
            get => _isEnabled;
            private set
            {
                if (_isEnabled == value)
                    return;
                _isEnabled = value;
                OnPropertyChangedWithValue(value, nameof(IsEnabled));
            }
        }

        [DataSourceProperty]
        public bool IsSpecial => false;

        [DataSourceProperty]
        public CoopHideoutBossConversationHintVM OptionHint { get; }

        [DataSourceProperty]
        public ViewModel PersuasionItem => null;

        public void ExecuteAction()
        {
            if (IsEnabled)
                _execute?.Invoke();
        }

        public void SetCurrentAnswer()
        {
        }

        public void ResetCurrentAnswer()
        {
        }

        public void DisableAfterSubmission()
        {
            IsEnabled = false;
        }
    }

    public sealed class CoopHideoutBossConversationHintVM : ViewModel
    {
        private readonly TextObject _hintText;

        public CoopHideoutBossConversationHintVM()
            : this(TextObject.GetEmpty())
        {
        }

        public CoopHideoutBossConversationHintVM(TextObject hintText)
        {
            _hintText = hintText ?? TextObject.GetEmpty();
        }

        public void ExecuteBeginHint()
        {
            if (!TextObject.IsNullOrEmpty(_hintText))
                MBInformationManager.ShowHint(_hintText.ToString());
        }

        public void ExecuteEndHint()
        {
            MBInformationManager.HideInformations();
        }
    }
}
