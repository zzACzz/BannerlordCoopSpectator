using CoopSpectator.Infrastructure;
using TaleWorlds.Library;

namespace CoopSpectator.UI
{
    // Stock SettlementOverlay bindings without native owners or action delegates.
    public sealed class CoopCampaignSettlementMenuVM : ViewModel
    {
        private CoopCampaignSettlementMenuPresentation _state;
        private readonly System.Action<string, bool> _hover;
        private readonly System.Collections.Generic.Dictionary<string, CoopSettlementHintVM> _hints =
            new System.Collections.Generic.Dictionary<string, CoopSettlementHintVM>(System.StringComparer.Ordinal);
        public CoopCampaignSettlementMenuVM() : this(null) { }
        public CoopCampaignSettlementMenuVM(System.Action<string, bool> hover)
        {
            _hover = hover;
            foreach (string key in CoopCampaignSettlementMenuPresentation.HintNames) _hints.Add(key, new CoopSettlementHintVM(hover));
        }
        [DataSourceProperty] public CoopConversationHudImageVM SettlementOwnerBanner { get; private set; }
        [DataSourceProperty] public MBBindingList<CoopCampaignMapEncounterPartyVM> CharacterList { get; } = new MBBindingList<CoopCampaignMapEncounterPartyVM>();
        [DataSourceProperty] public MBBindingList<CoopCampaignMapEncounterPartyVM> PartyList { get; } = new MBBindingList<CoopCampaignMapEncounterPartyVM>();
        [DataSourceProperty] public MBBindingList<ViewModel> ContextList { get; } = new MBBindingList<ViewModel>();
        [DataSourceProperty] public bool IsContextMenuEnabled => false;
        [DataSourceProperty] public int CurrentOverlayType => 0;
        // The stock nested prefab defaults to visible when its data source is
        // absent. Keep an owned, explicitly hidden context even on read-only peers.
        [DataSourceProperty] public CoopSettlementHiddenCardSelectionVM CardSelectionPopup { get; } = new CoopSettlementHiddenCardSelectionVM();
        [DataSourceProperty] public string SettlementNameLbl => _state?.Labels["SettlementNameLbl"] ?? string.Empty;
        [DataSourceProperty] public string RemainingFoodText => _state?.Labels["RemainingFoodText"] ?? string.Empty;
        [DataSourceProperty] public string MilitasLbl => _state?.Labels["MilitasLbl"] ?? string.Empty;
        [DataSourceProperty] public string GarrisonLbl => _state?.Labels["GarrisonLbl"] ?? string.Empty;
        [DataSourceProperty] public string CrimeLbl => _state?.Labels["CrimeLbl"] ?? string.Empty;
        [DataSourceProperty] public string ProsperityLbl => _state?.Labels["ProsperityLbl"] ?? string.Empty;
        [DataSourceProperty] public string LoyaltyLbl => _state?.Labels["LoyaltyLbl"] ?? string.Empty;
        [DataSourceProperty] public string SecurityLbl => _state?.Labels["SecurityLbl"] ?? string.Empty;
        [DataSourceProperty] public string WallsLbl => _state?.Labels["WallsLbl"] ?? string.Empty;
        [DataSourceProperty] public string ShipyardLbl => _state?.Labels["ShipyardLbl"] ?? string.Empty;
        [DataSourceProperty] public int WallsLevel => _state?.Numbers["WallsLevel"] ?? 0;
        [DataSourceProperty] public int ProsperityChangeAmount => _state?.Numbers["ProsperityChangeAmount"] ?? 0;
        [DataSourceProperty] public int MilitiaChangeAmount => _state?.Numbers["MilitiaChangeAmount"] ?? 0;
        [DataSourceProperty] public int GarrisonChangeAmount => _state?.Numbers["GarrisonChangeAmount"] ?? 0;
        [DataSourceProperty] public int GarrisonAmount => _state?.Numbers["GarrisonAmount"] ?? 0;
        [DataSourceProperty] public int CrimeChangeAmount => _state?.Numbers["CrimeChangeAmount"] ?? 0;
        [DataSourceProperty] public int LoyaltyChangeAmount => _state?.Numbers["LoyaltyChangeAmount"] ?? 0;
        [DataSourceProperty] public int SecurityChangeAmount => _state?.Numbers["SecurityChangeAmount"] ?? 0;
        [DataSourceProperty] public int FoodChangeAmount => _state?.Numbers["FoodChangeAmount"] ?? 0;
        [DataSourceProperty] public bool IsFortification => _state?.Flags["IsFortification"] ?? false;
        [DataSourceProperty] public bool IsCrimeEnabled => _state?.Flags["IsCrimeEnabled"] ?? false;
        [DataSourceProperty] public bool CanAssignMembers => _state?.Flags["CanAssignMembers"] ?? false;
        [DataSourceProperty] public bool IsNoGarrisonWarning => _state?.Flags["IsNoGarrisonWarning"] ?? false;
        [DataSourceProperty] public bool IsCrimeLabelHighlightEnabled => _state?.Flags["IsCrimeLabelHighlightEnabled"] ?? false;
        [DataSourceProperty] public bool IsLoyaltyRebellionWarning => _state?.Flags["IsLoyaltyRebellionWarning"] ?? false;
        [DataSourceProperty] public bool IsShipyardEnabled => _state?.Flags["IsShipyardEnabled"] ?? false;
        [DataSourceProperty] public bool IsInitializationOver => _state?.Flags["IsInitializationOver"] ?? false;
        [DataSourceProperty] public bool IsInfoBarExtended => _state?.Flags["IsInfoBarExtended"] ?? false;
        [DataSourceProperty] public ViewModel RemainingFoodHint => _hints["RemainingFoodHint"];
        [DataSourceProperty] public ViewModel MilitasHint => _hints["MilitasHint"];
        [DataSourceProperty] public ViewModel GarrisonHint => _hints["GarrisonHint"];
        [DataSourceProperty] public ViewModel ProsperityHint => _hints["ProsperityHint"];
        [DataSourceProperty] public ViewModel LoyaltyHint => _hints["LoyaltyHint"];
        [DataSourceProperty] public ViewModel SecurityHint => _hints["SecurityHint"];
        [DataSourceProperty] public ViewModel WallsHint => _hints["WallsHint"];
        [DataSourceProperty] public ViewModel CrimeHint => _hints["CrimeHint"];
        [DataSourceProperty] public ViewModel CharacterFilterHint => _hints["CharacterFilterHint"];
        [DataSourceProperty] public ViewModel PartyFilterHint => _hints["PartyFilterHint"];
        [DataSourceProperty] public ViewModel AssignMembersHint => _hints["AssignMembersHint"];
        [DataSourceProperty] public ViewModel ShipyardHint => _hints["ShipyardHint"];
        internal void Apply(CoopCampaignSettlementMenuPresentation state, CoopDetachedPortraitResources resources)
        {
            var copy = state.Clone();
            bool bannerChanged = _state?.BannerCode != copy.BannerCode || SettlementOwnerBanner == null;
            _state = copy;
            if (bannerChanged)
            {
                SettlementOwnerBanner?.OnFinalize();
                SettlementOwnerBanner = new CoopConversationHudImageVM(copy.BannerCode, true, true);
                OnPropertyChanged(nameof(SettlementOwnerBanner));
            }
            foreach (var hint in _hints) hint.Value.Apply(copy.HintTokens.TryGetValue(hint.Key, out var token) ? token : null);
            Update(CharacterList, copy.Characters, resources, "Character_");
            Update(PartyList, copy.Parties, resources, "Party_");
            foreach (string name in CoopCampaignSettlementMenuPresentation.LabelNames) OnPropertyChanged(name);
            foreach (string name in CoopCampaignSettlementMenuPresentation.NumberNames) OnPropertyChanged(name);
            foreach (string name in CoopCampaignSettlementMenuPresentation.FlagNames) OnPropertyChanged(name);
        }
        private void Update(MBBindingList<CoopCampaignMapEncounterPartyVM> target,
            System.Collections.Generic.List<CoopCampaignMapEncounterParty> source, CoopDetachedPortraitResources resources, string prefix)
        {
            while (target.Count > source.Count) { target[target.Count - 1].OnFinalize(); target.RemoveAt(target.Count - 1); }
            while (target.Count < source.Count) target.Add(new CoopCampaignMapEncounterPartyVM(_hover));
            for (int i = 0; i < source.Count; i++) target[i].Apply(source[i], resources,
                _state.HintTokens.TryGetValue(prefix + i.ToString(System.Globalization.CultureInfo.InvariantCulture), out var token) ? token : string.Empty);
        }
        public void ExecuteAddCompanion() { }
        public void ExecuteOnOverlayClosed() { }
        public void ExecuteOnOverlayOpened() { }
        public override void OnFinalize()
        {
            SettlementOwnerBanner?.OnFinalize();
            foreach (var p in CharacterList) p.OnFinalize();
            foreach (var p in PartyList) p.OnFinalize();
            CharacterList.Clear(); PartyList.Clear(); ContextList.Clear();
            foreach (var hint in _hints.Values) hint.OnFinalize();
            CardSelectionPopup.OnFinalize();
            base.OnFinalize();
        }
    }
    public sealed class CoopSettlementHintVM : ViewModel
    {
        private readonly System.Action<string, bool> _hover;
        private string _token;
        private bool _hovered;
        public CoopSettlementHintVM(System.Action<string, bool> hover) { _hover = hover; }
        internal void Apply(string token)
        {
            token = CoopCampaignSettlementMenuPresentation.IsHintToken(token) ? token : null;
            if (_token == token) return;
            if (_hovered && _token != null) _hover?.Invoke(_token, false);
            _token = token;
            if (_hovered && _token != null) _hover?.Invoke(_token, true);
        }
        public void ExecuteBeginHint() { if (_hovered) return; _hovered = true; if (_token != null) _hover?.Invoke(_token, true); }
        public void ExecuteEndHint() { if (!_hovered) return; _hovered = false; if (_token != null) _hover?.Invoke(_token, false); }
        public override void OnFinalize() { ExecuteEndHint(); _token = null; base.OnFinalize(); }
    }
    // Retains the parent binding context for native statistic wrappers. Native
    // tooltip delegates are intentionally absent; remote interests need their own
    // audited disclosure/side-effect boundary before enabling these callbacks.
    public sealed class CoopSettlementPassiveHintVM : ViewModel
    {
        public void ExecuteBeginHint() { }
        public void ExecuteEndHint() { }
    }

    // No native popup owner, campaign delegates, input registration or mutation.
    // This context exists solely to bind the stock prefab's hidden initial state.
    public sealed class CoopSettlementHiddenCardSelectionVM : ViewModel
    {
        [DataSourceProperty] public bool IsVisible => false;
        [DataSourceProperty] public bool IsDoneEnabled => false;
        [DataSourceProperty] public string Title => string.Empty;
        [DataSourceProperty] public string ActionResult => string.Empty;
        [DataSourceProperty] public string DoneLbl => string.Empty;
        [DataSourceProperty] public MBBindingList<ViewModel> Items { get; } = new MBBindingList<ViewModel>();
        [DataSourceProperty] public CoopSettlementHiddenInputKeyVM DoneInputKey { get; } = new CoopSettlementHiddenInputKeyVM();
        [DataSourceProperty] public CoopSettlementHiddenInputKeyVM CancelInputKey { get; } = new CoopSettlementHiddenInputKeyVM();
        [DataSourceProperty] public CoopSettlementPassiveHintVM DisabledHint { get; } = new CoopSettlementPassiveHintVM();
        public void ExecuteDone() { }
        public void ExecuteCancel() { }
        public override void OnFinalize()
        {
            Items.Clear(); DoneInputKey.OnFinalize(); CancelInputKey.OnFinalize(); DisabledHint.OnFinalize();
            base.OnFinalize();
        }
    }
    public sealed class CoopSettlementHiddenInputKeyVM : ViewModel
    {
        [DataSourceProperty] public bool IsVisible => false;
        [DataSourceProperty] public string KeyID => string.Empty;
    }
}
