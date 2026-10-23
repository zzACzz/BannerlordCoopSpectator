using System;
using CoopSpectator.Infrastructure;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.Core;
using TaleWorlds.Core.ViewModelCollection.ImageIdentifiers;
using TaleWorlds.ObjectSystem;

namespace CoopSpectator.UI
{
    // Compatible with the stock GameMenu movie, without native campaign owners.
    public sealed class CoopCampaignMapMenuVM : ViewModel
    {
        private CoopCampaignMapMenuPresentation _state;
        private readonly Action<string, bool> _tooltipHover;
        public CoopCampaignMapMenuVM(Action<string, bool> tooltipHover = null) { _tooltipHover = tooltipHover; }
        public void ClearTooltipHover() { foreach (var item in PlunderItems) item.ExecuteEndTooltip(); }
        [DataSourceProperty] public string MenuId => _state?.MenuId ?? string.Empty;
        [DataSourceProperty] public string TitleText => _state?.Title ?? string.Empty;
        [DataSourceProperty] public string ContextText => _state?.Context ?? string.Empty;
        [DataSourceProperty] public string Background => _state?.Background ?? string.Empty;
        [DataSourceProperty] public string BackgroundCopy => Background;
        [DataSourceProperty] public bool IsNight => _state?.IsNight ?? false;
        [DataSourceProperty] public bool IsEncounterMenu => _state?.IsEncounterMenu ?? false;
        [DataSourceProperty] public bool IsInSiegeMode => _state?.IsInSiegeMode ?? false;
        [DataSourceProperty] public MBBindingList<CoopCampaignMapMenuOptionVM> ItemList { get; } = new MBBindingList<CoopCampaignMapMenuOptionVM>();
        [DataSourceProperty] public MBBindingList<CoopCampaignMapMenuProgressVM> ProgressItemList { get; } = new MBBindingList<CoopCampaignMapMenuProgressVM>();
        [DataSourceProperty] public MBBindingList<CoopCampaignMapMenuPlunderVM> PlunderItems { get; } = new MBBindingList<CoopCampaignMapMenuPlunderVM>();

        public void Apply(CoopCampaignMapMenuPresentation state)
        {
            _state = state.Clone();
            foreach (var name in new[] { "MenuId", "TitleText", "ContextText", "Background", "BackgroundCopy",
                "IsNight", "IsEncounterMenu", "IsInSiegeMode" }) OnPropertyChanged(name);
            while (ItemList.Count > state.Options.Count) ItemList.RemoveAt(ItemList.Count - 1);
            while (ItemList.Count < state.Options.Count) ItemList.Add(new CoopCampaignMapMenuOptionVM());
            for (int i = 0; i < ItemList.Count; i++) ItemList[i].Apply(state.MenuId, state.Options[i]);
            while (ProgressItemList.Count > state.ProgressItems.Count) ProgressItemList.RemoveAt(ProgressItemList.Count - 1);
            while (ProgressItemList.Count < state.ProgressItems.Count) ProgressItemList.Add(new CoopCampaignMapMenuProgressVM());
            for (int i = 0; i < ProgressItemList.Count; i++) ProgressItemList[i].Apply(state.ProgressItems[i]);
            while (PlunderItems.Count > state.PlunderItems.Count)
            { PlunderItems[PlunderItems.Count - 1].OnFinalize(); PlunderItems.RemoveAt(PlunderItems.Count - 1); }
            while (PlunderItems.Count < state.PlunderItems.Count) PlunderItems.Add(new CoopCampaignMapMenuPlunderVM(_tooltipHover));
            for (int i = 0; i < PlunderItems.Count; i++) PlunderItems[i].Apply(state.PlunderItems[i]);
        }
        public void ExecuteLink(string link) { } // No encyclopedia/campaign command.
        public override void OnFinalize()
        {
            foreach (var item in PlunderItems) item.OnFinalize();
            PlunderItems.Clear(); ItemList.Clear(); ProgressItemList.Clear(); base.OnFinalize();
        }
    }

    public sealed class CoopCampaignMapMenuPlunderVM : ViewModel
    {
        private string _itemId;
        private string _tooltipId;
        private bool _hovered;
        private readonly Action<string, bool> _tooltipHover;
        public CoopCampaignMapMenuPlunderVM(Action<string, bool> tooltipHover = null) { _tooltipHover = tooltipHover; }
        [DataSourceProperty] public int Amount { get; private set; }
        [DataSourceProperty] public ItemImageIdentifierVM Visual { get; private set; }
        public void Apply(CoopCampaignMapMenuPlunder state)
        {
            if (_tooltipId != state.TooltipId)
            {
                bool wasHovered = _hovered;
                ExecuteEndTooltip();
                _tooltipId = state.TooltipId;
                if (wasHovered) ExecuteBeginTooltip();
            }
            if (_itemId != state.ItemId)
            {
                Visual?.OnFinalize();
                _itemId = state.ItemId;
                var objects = MBObjectManager.Instance;
                var item = objects?.GetObject<ItemObject>(_itemId);
                // Campaign trade goods may only exist under a preloaded mirror
                // identifier in multiplayer. Never register objects while rendering.
                if (item == null && objects != null &&
                    ExactCampaignRuntimeItemRegistry.TryResolvePreloadedMirrorItem(_itemId, out string mirror, out _))
                    item = objects.GetObject<ItemObject>(mirror);
                Visual = item == null ? null : new ItemImageIdentifierVM(item);
                OnPropertyChanged("Visual");
            }
            if (Amount != state.Amount) { Amount = state.Amount; OnPropertyChanged("Amount"); }
        }
        // Only request detached host rows; never invoke a campaign provider here.
        public void ExecuteBeginTooltip()
        { _hovered = true; if (!string.IsNullOrEmpty(_tooltipId)) _tooltipHover?.Invoke(_tooltipId, true); }
        public void ExecuteEndTooltip()
        { if (_hovered && !string.IsNullOrEmpty(_tooltipId)) _tooltipHover?.Invoke(_tooltipId, false); _hovered = false; }
        public override void OnFinalize() { ExecuteEndTooltip(); Visual?.OnFinalize(); Visual = null; base.OnFinalize(); }
    }

    public sealed class CoopCampaignMapMenuOptionVM : ViewModel
    {
        private CoopCampaignMapMenuOption _state = new CoopCampaignMapMenuOption();
        [DataSourceProperty] public string GameMenuStringId { get; private set; } = string.Empty;
        [DataSourceProperty] public string Item => _state.Text;
        [DataSourceProperty] public string OptionLeaveType => _state.LeaveType;
        [DataSourceProperty] public int ItemType => _state.ItemType;
        [DataSourceProperty] public int BattleSize => _state.BattleSize;
        [DataSourceProperty] public bool IsEnabled => _state.IsEnabled;
        [DataSourceProperty] public bool IsWaitActive => _state.IsWaitActive;
        [DataSourceProperty] public bool IsHighlightEnabled => _state.IsHighlightEnabled;
        [DataSourceProperty] public bool IsNavalBattle => _state.IsNavalBattle;
        [DataSourceProperty] public CoopCampaignMapConversationHintVM ItemHint { get; private set; } = new CoopCampaignMapConversationHintVM();
        [DataSourceProperty] public MBBindingList<ViewModel> Quests { get; } = new MBBindingList<ViewModel>();
        [DataSourceProperty] public ViewModel ShortcutKey => null;
        public void Apply(string menuId, CoopCampaignMapMenuOption state)
        {
            GameMenuStringId = menuId; _state = state.Clone();
            ItemHint = new CoopCampaignMapConversationHintVM(new TextObject("{=!}" + state.Hint), traceMenuHint: true);
            foreach (var name in new[] { "GameMenuStringId", "Item", "OptionLeaveType", "ItemType", "BattleSize",
                "IsEnabled", "IsWaitActive", "IsHighlightEnabled", "IsNavalBattle", "ItemHint" }) OnPropertyChanged(name);
        }
        public void ExecuteAction() { } // Preserve host appearance, never host authority.
    }

    public sealed class CoopCampaignMapMenuProgressVM : ViewModel
    {
        private CoopCampaignMapMenuProgress _state = new CoopCampaignMapMenuProgress();
        [DataSourceProperty] public string Text => _state.Text;
        [DataSourceProperty] public string ProgressText => _state.ProgressText;
        [DataSourceProperty] public float Progress => _state.Progress;
        public void Apply(CoopCampaignMapMenuProgress state)
        {
            _state = state.Clone();
            OnPropertyChanged("Text"); OnPropertyChanged("ProgressText"); OnPropertyChanged("Progress");
        }
    }
}
