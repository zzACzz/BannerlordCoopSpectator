using CoopSpectator.Infrastructure;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace CoopSpectator.UI
{
    public sealed class CoopCampaignMapConversationVM : ViewModel
    {
        public CoopCampaignMapConversationVM(
            CoopCampaignMapConversationState state)
        {
            CoopCampaignMapConversationState safe = state?.Clone() ??
                CoopCampaignMapPrototypeContract.CreateInactiveConversation();
            CurrentCharacterNameLbl = safe.SpeakerName ?? string.Empty;
            DialogText = safe.DialogText ?? string.Empty;
            GoldText = safe.GoldText ?? string.Empty;
            ContinueText = safe.ContinueText ?? string.Empty;
            GoldHint = new CoopCampaignMapConversationHintVM(
                new TextObject("{=o5G8A8ZH}Your Denars"));
            FactionHint = new CoopCampaignMapConversationHintVM();
            RelationHint = new CoopCampaignMapConversationHintVM();
            AnswerList =
                new MBBindingList<CoopCampaignMapConversationOptionVM>();
            AttackerParties =
                new MBBindingList<CoopCampaignMapConversationOptionVM>();
            DefenderParties =
                new MBBindingList<CoopCampaignMapConversationOptionVM>();
            if (safe.Options != null)
            {
                foreach (CoopCampaignMapConversationOptionState option in
                         safe.Options)
                {
                    if (option != null)
                    {
                        AnswerList.Add(
                            new CoopCampaignMapConversationOptionVM(option));
                    }
                }
            }
        }

        [DataSourceProperty]
        public string CurrentCharacterNameLbl { get; }

        [DataSourceProperty]
        public string DialogText { get; }

        [DataSourceProperty]
        public string GoldText { get; }

        [DataSourceProperty]
        public string ContinueText { get; }

        [DataSourceProperty]
        public string MoreOptionText => string.Empty;

        [DataSourceProperty]
        public string PersuasionText => string.Empty;

        [DataSourceProperty]
        public string RelationText => string.Empty;

        [DataSourceProperty]
        public int Relation => 0;

        [DataSourceProperty]
        public int MinRelation => -100;

        [DataSourceProperty]
        public int MaxRelation => 100;

        [DataSourceProperty]
        public int SelectedSide => 0;

        [DataSourceProperty]
        public CoopCampaignMapConversationHintVM GoldHint { get; }

        [DataSourceProperty]
        public CoopCampaignMapConversationHintVM FactionHint { get; }

        [DataSourceProperty]
        public CoopCampaignMapConversationHintVM RelationHint { get; }

        [DataSourceProperty]
        public MBBindingList<CoopCampaignMapConversationOptionVM> AnswerList
        {
            get;
        }

        [DataSourceProperty]
        public MBBindingList<CoopCampaignMapConversationOptionVM>
            AttackerParties { get; }

        [DataSourceProperty]
        public MBBindingList<CoopCampaignMapConversationOptionVM>
            DefenderParties { get; }

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
        public ViewModel CurrentSelectedAnswer => null;

        [DataSourceProperty]
        public bool IsAggressive => false;

        [DataSourceProperty]
        public bool IsPersuading => false;

        [DataSourceProperty]
        public bool IsBannerEnabled => false;

        [DataSourceProperty]
        public bool IsRelationEnabled => false;

        [DataSourceProperty]
        public bool IsCurrentCharacterValidInEncyclopedia => false;

        [DataSourceProperty]
        public bool IsLoadingOver => true;

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
            foreach (CoopCampaignMapConversationOptionVM option in AnswerList)
                option?.OnFinalize();
            AnswerList.Clear();
            base.OnFinalize();
        }
    }

    public sealed class CoopCampaignMapConversationOptionVM : ViewModel
    {
        public CoopCampaignMapConversationOptionVM(
            CoopCampaignMapConversationOptionState option)
        {
            ItemText = option?.Text ?? string.Empty;
            IsEnabled = option?.IsEnabled ?? false;
            IsSpecial = option?.IsSpecial ?? false;
            OptionHint = new CoopCampaignMapConversationHintVM(
                new TextObject("{=!}Only the campaign host can choose this response."));
        }

        [DataSourceProperty]
        public string ItemText { get; }

        [DataSourceProperty]
        public bool IsEnabled { get; }

        [DataSourceProperty]
        public bool IsSpecial { get; }

        [DataSourceProperty]
        public bool HasPersuasion => false;

        [DataSourceProperty]
        public int IconType => 0;

        [DataSourceProperty]
        public CoopCampaignMapConversationHintVM OptionHint { get; }

        [DataSourceProperty]
        public ViewModel PersuasionItem => null;

        public void ExecuteAction()
        {
            // Display-only milestone: no client choice leaves this process.
        }

        public void SetCurrentAnswer()
        {
        }

        public void ResetCurrentAnswer()
        {
        }
    }

    public sealed class CoopCampaignMapConversationHintVM : ViewModel
    {
        private readonly TextObject _hintText;

        public CoopCampaignMapConversationHintVM()
            : this(TextObject.GetEmpty())
        {
        }

        public CoopCampaignMapConversationHintVM(TextObject hintText)
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
