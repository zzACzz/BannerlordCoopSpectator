using System;
using System.Collections.Generic;
using CoopSpectator.Infrastructure;
using TaleWorlds.Library;

namespace CoopSpectator.UI
{
    // Stock MapBar movie bindings, without native campaign VMs or command authority.
    public sealed class CoopCampaignMapBarVM : ViewModel
    {
        private CoopCampaignMapBarPresentation _state = new CoopCampaignMapBarPresentation();
        private readonly Action _followHost;
        private readonly Action<string, bool> _hintHover;
        private bool? _lastHostExpanded;
        private readonly CoopCampaignMapBarTutorialVM _tutorial = new CoopCampaignMapBarTutorialVM();
        private readonly Dictionary<string, CoopCampaignMapBarHintVM> _controls = new Dictionary<string, CoopCampaignMapBarHintVM>(StringComparer.Ordinal);
        private CoopCampaignMapBarHintVM Control(string key)
        {
            if (!_controls.TryGetValue(key, out var hint)) _controls.Add(key, hint = new CoopCampaignMapBarHintVM(_hintHover));
            return hint;
        }
        public CoopCampaignMapBarVM(Action followHost, Action<string, bool> hintHover = null) { _followHost = followHost; _hintHover = hintHover; }
        public void ClearHintHover()
        {
            foreach (var item in PrimaryInfoItems) item.ExecuteEndHint();
            foreach (var item in SecondaryInfoItems) item.ExecuteEndHint();
            foreach (var hint in _controls.Values) hint.ExecuteEndHint();
            foreach (var item in NavigationItems) item.ClearHintHover();
        }
        [DataSourceProperty] public CoopCampaignMapBarVM MapTimeControl => this;
        [DataSourceProperty] public CoopCampaignMapBarVM MapInfo => this;
        [DataSourceProperty] public CoopCampaignMapBarVM MapNavigation => this;
        [DataSourceProperty] public CoopCampaignMapBarTutorialVM TutorialNotification => _tutorial;
        [DataSourceProperty] public CoopCampaignMapBarHintVM GatherArmyHint => Control("GatherArmyHint");
        [DataSourceProperty] public CoopCampaignMapBarHintVM ExtendHint => Control("ExtendHint");
        [DataSourceProperty] public CoopCampaignMapBarHintVM TimeOfDayHint => Control("TimeOfDayHint");
        [DataSourceProperty] public CoopCampaignMapBarHintVM PauseHint => Control("PauseHint");
        [DataSourceProperty] public CoopCampaignMapBarHintVM PlayHint => Control("PlayHint");
        [DataSourceProperty] public CoopCampaignMapBarHintVM FastForwardHint => Control("FastForwardHint");
        [DataSourceProperty] public bool IsEnabled => _state.IsEnabled;
        [DataSourceProperty] public bool IsGatherArmyVisible => _state.IsGatherArmyVisible;
        [DataSourceProperty] public bool CanGatherArmy => _state.CanGatherArmy;
        [DataSourceProperty] public bool IsCenterPanelEnabled => _state.IsCenterPanelEnabled;
        [DataSourceProperty] public bool IsInfoBarEnabled => _state.IsInfoBarEnabled;
        [DataSourceProperty] public bool IsInfoBarExtended
        {
            get => _state.IsInfoBarExtended;
            // Local expansion changes presentation only, never host campaign state.
            set { _state.IsInfoBarExtended = value; OnPropertyChanged(nameof(IsInfoBarExtended)); }
        }
        [DataSourceProperty] public string Date => _state.Date;
        [DataSourceProperty] public string PausedText => _state.PausedText;
        [DataSourceProperty] public double Time => _state.Time;
        [DataSourceProperty] public int TimeFlowState => _state.TimeFlowState;
        [DataSourceProperty] public MBBindingList<CoopCampaignMapBarInfoVM> PrimaryInfoItems { get; } = new MBBindingList<CoopCampaignMapBarInfoVM>();
        [DataSourceProperty] public MBBindingList<CoopCampaignMapBarInfoVM> SecondaryInfoItems { get; } = new MBBindingList<CoopCampaignMapBarInfoVM>();
        [DataSourceProperty] public MBBindingList<CoopCampaignMapBarNavigationVM> NavigationItems { get; } = new MBBindingList<CoopCampaignMapBarNavigationVM>();

        public void Apply(CoopCampaignMapBarPresentation state)
        {
            if (state == null || !state.IsValid()) throw new ArgumentException("Invalid map bar.");
            bool expanded = _lastHostExpanded.HasValue && _lastHostExpanded.Value == state.IsInfoBarExtended
                ? _state.IsInfoBarExtended : state.IsInfoBarExtended;
            _lastHostExpanded = state.IsInfoBarExtended;
            _state = state.Clone();
            _state.IsInfoBarExtended = expanded;
            _tutorial.Apply(_state.IsEnabled ? _state.TutorialElementId : string.Empty);
            foreach (string key in new[] { "GatherArmyHint", "ExtendHint", "TimeOfDayHint", "PauseHint", "PlayHint", "FastForwardHint" })
                Control(key).Apply(_state.ControlHints.Find(x => x.Key == key), _state.IsEnabled);
            Update(PrimaryInfoItems, _state.PrimaryInfoItems);
            Update(SecondaryInfoItems, _state.SecondaryInfoItems);
            while (NavigationItems.Count > _state.NavigationItems.Count)
            { NavigationItems[NavigationItems.Count - 1].OnFinalize(); NavigationItems.RemoveAt(NavigationItems.Count - 1); }
            while (NavigationItems.Count < _state.NavigationItems.Count) NavigationItems.Add(new CoopCampaignMapBarNavigationVM(_hintHover));
            for (int i = 0; i < NavigationItems.Count; i++)
            {
                var item = _state.NavigationItems[i];
                NavigationItems[i].Apply(item);
                NavigationItems[i].Tooltip.Apply(_state.ControlHints.Find(x => x.Key == "Navigation_" + item.ItemId), _state.IsEnabled);
                NavigationItems[i].AlertTooltip.Apply(_state.ControlHints.Find(x => x.Key == "Alert_" + item.ItemId), _state.IsEnabled && item.HasAlert);
            }
            foreach (string property in new[] { "IsEnabled", "IsGatherArmyVisible", "CanGatherArmy", "IsCenterPanelEnabled",
                "IsInfoBarEnabled", "IsInfoBarExtended", "Date", "PausedText", "Time", "TimeFlowState" }) OnPropertyChanged(property);
        }
        private void Update(MBBindingList<CoopCampaignMapBarInfoVM> target,
            System.Collections.Generic.List<CoopCampaignMapBarInfo> source)
        {
            while (target.Count > source.Count) { target[target.Count - 1].OnFinalize(); target.RemoveAt(target.Count - 1); }
            while (target.Count < source.Count) target.Add(new CoopCampaignMapBarInfoVM(_hintHover));
            for (int i = 0; i < target.Count; i++) target[i].Apply(source[i]);
        }
        public void ExecuteResetCamera() => _followHost?.Invoke();
        public void ExecuteTimeControlChange(int selectedTimeSpeed) { }
        public void ExecuteArmyManagement() { }
        public override void OnFinalize()
        {
            foreach (var item in PrimaryInfoItems) item.OnFinalize();
            foreach (var item in SecondaryInfoItems) item.OnFinalize();
            foreach (var item in NavigationItems) item.OnFinalize();
            PrimaryInfoItems.Clear(); SecondaryInfoItems.Clear(); NavigationItems.Clear();
            foreach (var hint in _controls.Values) hint.OnFinalize();
            _controls.Clear(); base.OnFinalize();
            _tutorial.Apply(string.Empty);
        }
    }

    public sealed class CoopCampaignMapBarTutorialVM : ViewModel
    {
        private string _elementId = string.Empty;
        [DataSourceProperty] public string ElementID => _elementId;
        public void Apply(string elementId)
        {
            if (_elementId == elementId) return;
            _elementId = elementId;
            OnPropertyChanged(nameof(ElementID));
        }
    }

    // Only published, available opaque targets can initiate read-only interest.
    public sealed class CoopCampaignMapBarHintVM : ViewModel
    {
        [DataSourceProperty] public string ElementID => string.Empty;
        private readonly Action<string, bool> _hover;
        private string _token;
        private bool _hovered;
        public CoopCampaignMapBarHintVM(Action<string, bool> hover = null) { _hover = hover; }
        public void Apply(CoopCampaignMapBarControlTarget target, bool visible)
        {
            string token = visible && target?.IsAvailable == true && target.IsValid() ? target.HintToken : null;
            if (_token != token) ExecuteEndHint();
            _token = token;
        }
        public void ExecuteBeginHint()
        { if (_token != null && !_hovered) { _hovered = true; _hover?.Invoke(_token, true); } }
        public void ExecuteEndHint()
        { if (_hovered) { _hovered = false; _hover?.Invoke(_token, false); } }
        public override void OnFinalize() { ExecuteEndHint(); _token = null; base.OnFinalize(); }
    }
    public sealed class CoopCampaignMapBarInfoVM : ViewModel
    {
        private CoopCampaignMapBarInfo _state = new CoopCampaignMapBarInfo();
        private readonly Action<string, bool> _hover;
        private bool _hovered;
        public CoopCampaignMapBarInfoVM(Action<string, bool> hover = null) { _hover = hover; }
        [DataSourceProperty] public string VisualId => _state.VisualId;
        [DataSourceProperty] public string Value => _state.Value;
        [DataSourceProperty] public int IntValue => _state.IntValue;
        [DataSourceProperty] public bool HasWarning => _state.HasWarning;
        public void Apply(CoopCampaignMapBarInfo state)
        {
            if (_state.HintToken != state.HintToken) ExecuteEndHint();
            _state = state.Clone();
            foreach (string name in new[] { "VisualId", "Value", "IntValue", "HasWarning" }) OnPropertyChanged(name);
        }
        public void ExecuteBeginHint()
        { if (CoopCampaignMapBarHintIdentity<object>.IsToken(_state.HintToken)) { _hovered = true; _hover?.Invoke(_state.HintToken, true); } }
        public void ExecuteEndHint()
        { if (_hovered) { _hovered = false; _hover?.Invoke(_state.HintToken, false); } }
        public override void OnFinalize() { ExecuteEndHint(); base.OnFinalize(); }
    }
    public sealed class CoopCampaignMapBarNavigationVM : ViewModel
    {
        private CoopCampaignMapBarNavigation _state = new CoopCampaignMapBarNavigation();
        public CoopCampaignMapBarNavigationVM(Action<string, bool> hover = null)
        { Tooltip = new CoopCampaignMapBarHintVM(hover); AlertTooltip = new CoopCampaignMapBarHintVM(hover); }
        [DataSourceProperty] public string ItemId => _state.ItemId;
        [DataSourceProperty] public string AlertText => _state.AlertText;
        [DataSourceProperty] public bool IsEnabled => _state.IsEnabled;
        [DataSourceProperty] public bool IsActive => _state.IsActive;
        [DataSourceProperty] public bool HasAlert => _state.HasAlert;
        [DataSourceProperty] public CoopCampaignMapBarHintVM Tooltip { get; }
        [DataSourceProperty] public CoopCampaignMapBarHintVM AlertTooltip { get; }
        public void ClearHintHover() { Tooltip.ExecuteEndHint(); AlertTooltip.ExecuteEndHint(); }
        public void Apply(CoopCampaignMapBarNavigation state)
        {
            _state = state.Clone();
            foreach (string name in new[] { "ItemId", "AlertText", "IsEnabled", "IsActive", "HasAlert" }) OnPropertyChanged(name);
        }
        public void ExecuteOpen() { }
        public void ExecuteGoToLink() { }
        public override void OnFinalize() { Tooltip.OnFinalize(); AlertTooltip.OnFinalize(); base.OnFinalize(); }
    }
}
