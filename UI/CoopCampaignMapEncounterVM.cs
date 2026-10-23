using CoopSpectator.Infrastructure;
using TaleWorlds.Library;

namespace CoopSpectator.UI
{
    // Stock EncounterOverlay bindings, detached from campaign owners/actions.
    public sealed class CoopCampaignMapEncounterVM : ViewModel
    {
        private readonly System.Action<string, bool> _hover;
        public CoopCampaignMapEncounterVM(System.Action<string, bool> hover = null) { _hover = hover; }
        public void ClearTooltipHover()
        {
            foreach (var p in AttackerPartyList) p.ExecuteCloseTooltip();
            foreach (var p in DefenderPartyList) p.ExecuteCloseTooltip();
        }
        private CoopCampaignMapEncounterPresentation _state;
        [DataSourceProperty] public string TitleText => _state.TitleText;
        [DataSourceProperty] public bool IsSiege => _state.IsSiege;
        [DataSourceProperty] public bool IsNaval => _state.IsNaval;
        [DataSourceProperty] public string AttackerPartyCountLbl => _state.AttackerCountLabel;
        [DataSourceProperty] public string DefenderPartyCountLbl => _state.DefenderCountLabel;
        [DataSourceProperty] public string AttackerPartyMorale => _state.AttackerMorale;
        [DataSourceProperty] public string DefenderPartyMorale => _state.DefenderMorale;
        [DataSourceProperty] public string AttackerPartyFood => _state.AttackerFood;
        [DataSourceProperty] public string DefenderPartyFood => _state.DefenderFood;
        [DataSourceProperty] public string DefenderWallHitPoints => _state.DefenderWallHitPoints;
        [DataSourceProperty] public int AttackerShipCount => _state.AttackerShipCount;
        [DataSourceProperty] public int DefenderShipCount => _state.DefenderShipCount;
        [DataSourceProperty] public CoopConversationHudImageVM AttackerPartyBanner { get; private set; }
        [DataSourceProperty] public CoopConversationHudImageVM DefenderPartyBanner { get; private set; }
        [DataSourceProperty] public CoopConversationHudPowerVM PowerComparer { get; private set; }
        // Stock encounter disables these hint widgets. Do not bind native
        // campaign tooltip delegates; synchronized tooltip payload is a later gate.
        [DataSourceProperty] public ViewModel AttackerBannerHint => null;
        [DataSourceProperty] public ViewModel DefenderBannerHint => null;
        [DataSourceProperty] public ViewModel AttackerFoodHint => null;
        [DataSourceProperty] public ViewModel DefenderFoodHint => null;
        [DataSourceProperty] public ViewModel AttackerMoraleHint => null;
        [DataSourceProperty] public ViewModel DefenderMoraleHint => null;
        [DataSourceProperty] public ViewModel AttackerTroopNumHint => null;
        [DataSourceProperty] public ViewModel DefenderTroopNumHint => null;
        [DataSourceProperty] public ViewModel AttackerShipNumHint => null;
        [DataSourceProperty] public ViewModel DefenderShipNumHint => null;
        [DataSourceProperty] public ViewModel DefenderWallHint => null;
        [DataSourceProperty] public MBBindingList<CoopCampaignMapEncounterPartyVM> AttackerPartyList { get; } = new MBBindingList<CoopCampaignMapEncounterPartyVM>();
        [DataSourceProperty] public MBBindingList<CoopCampaignMapEncounterPartyVM> DefenderPartyList { get; } = new MBBindingList<CoopCampaignMapEncounterPartyVM>();

        public void Apply(CoopCampaignMapEncounterPresentation state)
        {
            if (!state.TrySnapshot(out var copy)) throw new System.ArgumentException("Invalid encounter presentation.");
            _state = copy;
            AttackerPartyBanner?.OnFinalize(); DefenderPartyBanner?.OnFinalize(); PowerComparer?.OnFinalize();
            AttackerPartyBanner = new CoopConversationHudImageVM(copy.AttackerBanner, true, true);
            DefenderPartyBanner = new CoopConversationHudImageVM(copy.DefenderBanner, true, true);
            PowerComparer = new CoopConversationHudPowerVM(new CoopCampaignMapConversationHud
            {
                AttackerPower = copy.AttackerPower, DefenderPower = copy.DefenderPower,
                InitialAttackerPower = copy.InitialAttackerPower, InitialDefenderPower = copy.InitialDefenderPower,
                AttackerColor = copy.AttackerColor, DefenderColor = copy.DefenderColor
            });
            UpdateParties(AttackerPartyList, copy.Attackers);
            UpdateParties(DefenderPartyList, copy.Defenders);
            foreach (var name in new[] { "TitleText", "IsSiege", "IsNaval", "AttackerPartyCountLbl", "DefenderPartyCountLbl",
                "AttackerPartyMorale", "DefenderPartyMorale", "AttackerPartyFood", "DefenderPartyFood", "DefenderWallHitPoints",
                "AttackerShipCount", "DefenderShipCount", "AttackerPartyBanner", "DefenderPartyBanner", "PowerComparer" }) OnPropertyChanged(name);
        }
        private void UpdateParties(MBBindingList<CoopCampaignMapEncounterPartyVM> target,
            System.Collections.Generic.List<CoopCampaignMapEncounterParty> source)
        {
            while (target.Count > source.Count) { target[target.Count - 1].OnFinalize(); target.RemoveAt(target.Count - 1); }
            while (target.Count < source.Count) target.Add(new CoopCampaignMapEncounterPartyVM(_hover));
            for (int i = 0; i < source.Count; i++) target[i].Apply(source[i]);
        }
        public override void OnFinalize()
        {
            AttackerPartyBanner?.OnFinalize(); DefenderPartyBanner?.OnFinalize(); PowerComparer?.OnFinalize();
            foreach (var p in AttackerPartyList) p.OnFinalize();
            foreach (var p in DefenderPartyList) p.OnFinalize();
            AttackerPartyList.Clear(); DefenderPartyList.Clear(); base.OnFinalize();
        }
    }

    public sealed class CoopCampaignMapEncounterPartyVM : ViewModel
    {
        private readonly System.Action<string, bool> _hover;
        private bool _isHovered;
        public CoopCampaignMapEncounterPartyVM(System.Action<string, bool> hover = null) { _hover = hover; }
        private CoopCampaignMapEncounterParty _state = new CoopCampaignMapEncounterParty();
        [DataSourceProperty] public string NameText => _state.NameText;
        [DataSourceProperty] public string PartySizeLbl => _state.PartySizeLabel;
        [DataSourceProperty] public string DescriptionText => _state.DescriptionText;
        [DataSourceProperty] public string LocationText => _state.LocationText;
        [DataSourceProperty] public string PowerText => _state.PowerText;
        [DataSourceProperty] public string ProfessionText => _state.ProfessionText;
        // Stock GameMenuPartyItem binds this to HoveredCursorState. Gauntlet
        // parses every non-null value as MouseCursors; an empty string crashes.
        // A normal cursor does not advertise the intentionally inert link action.
        [DataSourceProperty] public string EncyclopediaCursorEffect => "Default";
        [DataSourceProperty] public int Relation => _state.Relation;
        [DataSourceProperty] public int ShipCount => _state.ShipCount;
        [DataSourceProperty] public bool IsPlayer => _state.IsPlayer;
        [DataSourceProperty] public bool IsEnemy => _state.IsEnemy;
        [DataSourceProperty] public bool IsAlly => _state.IsAlly;
        [DataSourceProperty] public bool IsNeutral => _state.IsNeutral;
        [DataSourceProperty] public bool IsCharacterInPrison => _state.IsCharacterInPrison;
        [DataSourceProperty] public bool IsHighlightEnabled => _state.IsHighlightEnabled;
        [DataSourceProperty] public CoopConversationHudImageVM Visual { get; private set; }
        [DataSourceProperty] public CoopConversationHudImageVM Banner_9 { get; private set; }
        [DataSourceProperty] public MBBindingList<CoopConversationHudQuestVM> Quests { get; } = new MBBindingList<CoopConversationHudQuestVM>();
        public void Apply(CoopCampaignMapEncounterParty state)
        {
            bool hoverChanged = _state.TooltipId != state.TooltipId;
            if (_isHovered && hoverChanged) _hover?.Invoke(_state.TooltipId, false);
            bool portraitChanged = Visual == null || _state.PortraitCode != state.PortraitCode || _state.PortraitPose != state.PortraitPose;
            bool bannerChanged = Banner_9 == null || _state.BannerCode != state.BannerCode;
            _state = state.Clone();
            if (_isHovered && hoverChanged && !string.IsNullOrEmpty(_state.TooltipId)) _hover?.Invoke(_state.TooltipId, true);
            if (portraitChanged) { Visual?.OnFinalize(); Visual = new CoopConversationHudImageVM(state.PortraitCode, false, portraitPose: state.PortraitPose); OnPropertyChanged("Visual"); }
            if (bannerChanged) { Banner_9?.OnFinalize(); Banner_9 = new CoopConversationHudImageVM(state.BannerCode, true, true); OnPropertyChanged("Banner_9"); }
            foreach (var q in Quests) q.OnFinalize();
            Quests.Clear(); foreach (int q in state.QuestMarkers) Quests.Add(new CoopConversationHudQuestVM(q));
            foreach (var name in new[] { "NameText", "PartySizeLbl", "DescriptionText", "LocationText", "PowerText", "ProfessionText",
                "Relation", "ShipCount", "IsPlayer", "IsEnemy", "IsAlly", "IsNeutral", "IsCharacterInPrison", "IsHighlightEnabled" }) OnPropertyChanged(name);
        }
        public void ExecuteOpenEncyclopedia() { }
        public void ExecuteSetAsContextMenuItem() { }
        public void ExecuteOpenTooltip()
        {
            _isHovered = true;
            if (!string.IsNullOrEmpty(_state.TooltipId)) _hover?.Invoke(_state.TooltipId, true);
        }
        public void ExecuteCloseTooltip()
        {
            if (_isHovered) _hover?.Invoke(_state.TooltipId, false);
            _isHovered = false;
        }
        public override void OnFinalize()
        {
            ExecuteCloseTooltip();
            Visual?.OnFinalize(); Banner_9?.OnFinalize();
            foreach (var q in Quests) q.OnFinalize();
            Quests.Clear(); base.OnFinalize();
        }
    }
}
