using System;
using CoopSpectator.Infrastructure;
using TaleWorlds.Library;
using TaleWorlds.Core.ViewModelCollection.Information.RundownTooltip;

namespace CoopSpectator.UI
{
    // Stock RundownTooltip movie bindings without finance delegates, campaign
    // refresh timers or global tooltip registration. Owner controls lifetime.
    public sealed class CoopCampaignMapRundownVM : TooltipBaseVM
    {
        private CoopCampaignMapRundownPresentation _state = new CoopCampaignMapRundownPresentation();
        public CoopCampaignMapRundownVM(Type invokedType, object[] invokedArgs) : base(invokedType, invokedArgs)
        { InvokeRefreshData(this); }
        protected override void OnIsExtendedChanged() { IsActive = false; }
        [DataSourceProperty] public int ValueCategorizationAsInt => _state.ValueCategorizationAsInt;
        [DataSourceProperty] public string TitleText => _state.TitleText;
        [DataSourceProperty] public string ExpectedChangeText => _state.ExpectedChangeText;
        [DataSourceProperty] public string ExtendText => _state.ExtendText;
        [DataSourceProperty] public MBBindingList<RundownLineVM> Lines { get; } = new MBBindingList<RundownLineVM>();

        public void Apply(CoopCampaignMapRundownPresentation state)
        {
            if (state == null || !state.IsValid()) throw new ArgumentException("Invalid rundown presentation.");
            _state = state.Clone();
            IsExtended = _state.IsExtended;
            while (Lines.Count > _state.Lines.Count)
            { Lines[Lines.Count - 1].OnFinalize(); Lines.RemoveAt(Lines.Count - 1); }
            while (Lines.Count < _state.Lines.Count) Lines.Add(new RundownLineVM(string.Empty, 0));
            for (int i = 0; i < Lines.Count; i++)
            {
                Lines[i].Name = _state.Lines[i].Name;
                Lines[i].Value = _state.Lines[i].Value;
                Lines[i].ValueAsString = _state.Lines[i].ValueAsString;
            }
            foreach (string property in new[] { "IsActive", "IsExtended", "ValueCategorizationAsInt",
                "TitleText", "ExpectedChangeText", "ExtendText" }) OnPropertyChanged(property);
            IsActive = _state.IsActive;
        }

        public override void OnFinalize()
        {
            IsActive = false;
            foreach (var line in Lines) line.OnFinalize();
            Lines.Clear();
            _state = new CoopCampaignMapRundownPresentation();
            base.OnFinalize();
        }
    }
}
