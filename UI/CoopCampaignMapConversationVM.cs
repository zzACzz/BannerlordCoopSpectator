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
            FactionHint = new CoopCampaignMapConversationHintVM(new TextObject("{=!}" + (safe.Hud?.FactionHint ?? string.Empty)));
            RelationHint = new CoopCampaignMapConversationHintVM();
            AnswerList =
                new MBBindingList<CoopCampaignMapConversationOptionVM>();
            AttackerParties =
                new MBBindingList<CoopConversationHudPartyVM>();
            DefenderParties =
                new MBBindingList<CoopConversationHudPartyVM>();
            var hud = safe.IsActive && CoopConversationHudCodec.IsValid(safe.Hud) ? safe.Hud : null;
            IsAggressive = hud?.IsAggressive ?? false;
            IsBannerEnabled = hud?.IsBannerEnabled ?? false;
            if (IsAggressive)
            {
                AttackerLeader = new CoopConversationHudPartyVM(hud.AttackerLeader);
                DefenderLeader = new CoopConversationHudPartyVM(hud.DefenderLeader);
                PowerComparer = new CoopConversationHudPowerVM(hud);
                foreach (var party in hud.AttackerParties) AttackerParties.Add(new CoopConversationHudPartyVM(party));
                foreach (var party in hud.DefenderParties) DefenderParties.Add(new CoopConversationHudPartyVM(party));
            }
            if (IsBannerEnabled) ConversedHeroBanner = new CoopConversationHudImageVM(hud.BannerCode, true, hud.BannerNineGrid);
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
        public MBBindingList<CoopConversationHudPartyVM>
            AttackerParties { get; }

        [DataSourceProperty]
        public MBBindingList<CoopConversationHudPartyVM>
            DefenderParties { get; }

        [DataSourceProperty]
        public CoopConversationHudPartyVM AttackerLeader { get; }

        [DataSourceProperty]
        public CoopConversationHudPartyVM DefenderLeader { get; }

        [DataSourceProperty]
        public CoopConversationHudPowerVM PowerComparer { get; }

        [DataSourceProperty]
        public ViewModel Persuasion => null;

        [DataSourceProperty]
        public CoopConversationHudImageVM ConversedHeroBanner { get; }

        [DataSourceProperty]
        public ViewModel CurrentSelectedAnswer => null;

        [DataSourceProperty]
        public bool IsAggressive { get; }

        [DataSourceProperty]
        public bool IsPersuading => false;

        [DataSourceProperty]
        public bool IsBannerEnabled { get; }

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
            foreach (var party in AttackerParties) party.OnFinalize();
            foreach (var party in DefenderParties) party.OnFinalize();
            AttackerParties.Clear(); DefenderParties.Clear();
            AttackerLeader?.OnFinalize(); DefenderLeader?.OnFinalize();
            PowerComparer?.OnFinalize(); ConversedHeroBanner?.OnFinalize();
            FactionHint.OnFinalize();
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
            OptionHint = new CoopCampaignMapConversationHintVM();
            // Native ConversationItem binds the nested fields even for ordinary
            // answers. Null leaves its blocking/progress widgets visible by default.
            PersuasionItem = new CoopCampaignMapConversationEmptyPersuasionVM();
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
        public CoopCampaignMapConversationEmptyPersuasionVM PersuasionItem { get; }

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

    // Display-only baseline for non-persuasion answers. Actual persuasion state
    // is not yet transported; this must not be presented as persuasion parity.
    public sealed class CoopCampaignMapConversationEmptyPersuasionVM : ViewModel
    {
        [DataSourceProperty]
        public bool IsPersuasionResultReady => false;

        [DataSourceProperty]
        public int PersuasionResultIndex => -1;

        [DataSourceProperty]
        public bool IsABlockingOption => false;

        [DataSourceProperty]
        public bool IsAProgressingOption => false;

        [DataSourceProperty]
        public int CritFailChance => 0;

        [DataSourceProperty]
        public int FailChance => 0;

        [DataSourceProperty]
        public int SuccessChance => 0;

        [DataSourceProperty]
        public int CritSuccessChance => 0;

        [DataSourceProperty]
        public string CritFailChanceText => string.Empty;

        [DataSourceProperty]
        public string FailChanceText => string.Empty;

        [DataSourceProperty]
        public string SuccessChanceText => string.Empty;

        [DataSourceProperty]
        public string CritSuccessChanceText => string.Empty;

        [DataSourceProperty]
        public CoopCampaignMapConversationHintVM CritFailHint { get; } =
            new CoopCampaignMapConversationHintVM();

        [DataSourceProperty]
        public CoopCampaignMapConversationHintVM FailHint { get; } =
            new CoopCampaignMapConversationHintVM();

        [DataSourceProperty]
        public CoopCampaignMapConversationHintVM SuccessHint { get; } =
            new CoopCampaignMapConversationHintVM();

        [DataSourceProperty]
        public CoopCampaignMapConversationHintVM CritSuccessHint { get; } =
            new CoopCampaignMapConversationHintVM();

        [DataSourceProperty]
        public CoopCampaignMapConversationHintVM BlockingOptionHint { get; } =
            new CoopCampaignMapConversationHintVM();

        [DataSourceProperty]
        public CoopCampaignMapConversationHintVM ProgressingOptionHint { get; } =
            new CoopCampaignMapConversationHintVM();

        public void ExecuteReadyToContinue()
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
