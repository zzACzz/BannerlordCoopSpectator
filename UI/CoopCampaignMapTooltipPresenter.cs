using System;
using CoopSpectator.Infrastructure;
using CoopSpectator.MissionBehaviors;
using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;

namespace CoopSpectator.UI
{
    // Uses the game's global tooltip movie, but only detached host-formatted rows.
    // No campaign provider or live campaign object is registered on the client.
    internal sealed class CoopCampaignMapTooltipPresenter : IDisposable
    {
        private sealed class ReplicaTooltip { }
        private sealed class ReplicaRundownTooltip { }
        private sealed class ReplicaHintTooltip { }
        private static readonly Action<HintVM, object[]> HintRenderer = RefreshHint;
        private static readonly Action<PropertyBasedTooltipVM, object[]> Renderer = Refresh;
        private static readonly Action<CoopCampaignMapRundownVM, object[]> RundownRenderer = RefreshRundown;
        private static CoopCampaignMapTooltipPresenter _registeredOwner;
        private PropertyBasedTooltipVM _viewModel;
        private HintVM _hintViewModel;
        private CoopCampaignMapRundownVM _rundownViewModel;
        private CoopCampaignMapTooltipPayload _payload;
        private bool _ownsVisible, _creating, _disposed, _faulted;
        private float _extendTime;

        public CoopCampaignMapTooltipPresenter()
        {
            // An older mission must release its registration before replacement.
            _registeredOwner?.Dispose();
            InformationManager.RegisterTooltip<ReplicaTooltip, PropertyBasedTooltipVM>(Renderer, "PropertyBasedTooltip");
            InformationManager.RegisterTooltip<ReplicaRundownTooltip, CoopCampaignMapRundownVM>(RundownRenderer, "RundownTooltip");
            InformationManager.RegisterTooltip<ReplicaHintTooltip, HintVM>(HintRenderer, "HintTooltip");
            _registeredOwner = this;
            InformationManager.OnShowTooltip += OnShow;
            InformationManager.OnHideTooltip += OnHide;
        }

        public void Tick(float dt, string hoveredId)
        {
            if (_disposed || _faulted) return;
            try
            {
                // These are the installed GauntletInformationView extension keys.
                bool held = Input.IsKeyDown((InputKey)56) || Input.IsKeyDown((InputKey)184) || Input.IsKeyDown((InputKey)248);
                _extendTime = hoveredId != null && held ? _extendTime + Math.Max(0f, Math.Min(dt, 0.1f)) : 0f;
                bool extended = Input.IsGamepadActive ? _extendTime > 0.18f : _extendTime > 0f;
                CoopCampaignMapPrototypeNetworkController.SetClientTooltipInterest(hoveredId, extended);
                var payload = CoopCampaignMapPrototypeNetworkController.CurrentClientTooltip;
                if (hoveredId == null || payload == null || payload.EntityId != hoveredId || payload.IsExtended != extended)
                { HideOwned(); return; }
                if (!_ownsVisible && InformationManager.GetIsAnyTooltipActive()) return;
                if (_ownsVisible && ReferenceEquals(_payload, payload)) return;
                bool samePresentation = HasSamePresentation(_payload, payload);
                if (_ownsVisible && _payload != null &&
                    (_payload.Kind != payload.Kind || IsPlainHint(_payload) != IsPlainHint(payload))) HideOwned();
                _payload = payload;
                if (IsPlainHint(payload))
                {
                    if (_ownsVisible && _hintViewModel != null)
                    {
                        if (!samePresentation) _hintViewModel.Text = payload.ControlHint.Text;
                        _hintViewModel.IsActive = true;
                    }
                    else
                    {
                        _creating = true;
                        try { InformationManager.ShowTooltip(typeof(ReplicaHintTooltip), this); }
                        finally { _creating = false; }
                    }
                    return;
                }
                if (payload.Kind == CoopCampaignMapTooltipKind.BarRundown)
                {
                    if (_ownsVisible && _rundownViewModel != null)
                    {
                        if (!samePresentation || !_rundownViewModel.IsActive || _rundownViewModel.IsExtended != payload.IsExtended)
                            _rundownViewModel.Apply(payload.Rundown);
                    }
                    else
                    {
                        _creating = true;
                        try { InformationManager.ShowTooltip(typeof(ReplicaRundownTooltip), this); }
                        finally { _creating = false; }
                    }
                    return;
                }
                if (_ownsVisible && _viewModel != null)
                {
                    // Keep unchanged rows and visibility: the native widget
                    // restarts its fade animation on a visibility transition.
                    if (samePresentation && _viewModel.IsActive && _viewModel.IsExtended == payload.IsExtended) return;
                    if (_viewModel.IsExtended != payload.IsExtended)
                    {
                        _viewModel.IsActive = false;
                        _viewModel.IsExtended = payload.IsExtended;
                    }
                    CopyRows(_viewModel, payload);
                }
                else
                {
                    _creating = true;
                    try { InformationManager.ShowTooltip(typeof(ReplicaTooltip), this); }
                    finally { _creating = false; }
                }
            }
            catch (Exception ex)
            {
                _faulted = true;
                Clear();
                ModLogger.Info("Campaign map tooltip presentation disabled: " + ex.GetType().Name);
            }
        }

        public void Clear()
        {
            _extendTime = 0f;
            CoopCampaignMapPrototypeNetworkController.ClearClientTooltip();
            HideOwned();
        }

        // The fullscreen inventory owns the shared per-client interest while it
        // is visible. Hide only our map movie, not its pending inventory request.
        public void SuspendPresentation() { _extendTime = 0f; HideOwned(); }

        private static void Refresh(PropertyBasedTooltipVM vm, object[] args)
        {
            if (args == null || args.Length != 1 || !(args[0] is CoopCampaignMapTooltipPresenter owner) ||
                owner._disposed || owner._faulted || !ReferenceEquals(_registeredOwner, owner)) return;
            owner._viewModel = vm;
            vm.IsActive = false;
            vm.TooltipPropertyList.Clear();
            var payload = owner._payload;
            if (payload == null) return;
            if (owner._creating) vm.IsExtended = payload.IsExtended;
            // A local extension transition must not reuse the other mode's rows.
            if (vm.IsExtended != payload.IsExtended) return;
            CopyRows(vm, payload);
        }

        private static void CopyRows(PropertyBasedTooltipVM vm, CoopCampaignMapTooltipPayload payload)
        {
            vm.TooltipPropertyList.Clear();
            vm.Mode = payload.Mode;
            foreach (var row in payload.Kind == CoopCampaignMapTooltipKind.BarControl ? payload.ControlHint.Rows : payload.Rows)
                vm.TooltipPropertyList.Add(new TooltipProperty(row.Definition, row.Value, row.TextHeight,
                    new Color(row.Red, row.Green, row.Blue, row.Alpha), false,
                    (TooltipProperty.TooltipPropertyFlags)row.PropertyModifier));
            vm.IsActive = vm.TooltipPropertyList.Count > 0;
        }

        private static void RefreshRundown(CoopCampaignMapRundownVM vm, object[] args)
        {
            vm.IsActive = false;
            if (args == null || args.Length != 1 || !(args[0] is CoopCampaignMapTooltipPresenter owner) ||
                owner._disposed || owner._faulted || !ReferenceEquals(_registeredOwner, owner) ||
                owner._payload?.Kind != CoopCampaignMapTooltipKind.BarRundown) return;
            owner._rundownViewModel = vm;
            if (owner._creating || vm.IsExtended == owner._payload.IsExtended) vm.Apply(owner._payload.Rundown);
        }

        private static bool IsPlainHint(CoopCampaignMapTooltipPayload payload) =>
            payload?.Kind == CoopCampaignMapTooltipKind.BarControl && !string.IsNullOrEmpty(payload.ControlHint?.Text);

        private static void RefreshHint(HintVM vm, object[] args)
        {
            if (args == null || args.Length != 1 || !(args[0] is CoopCampaignMapTooltipPresenter owner) ||
                owner._disposed || owner._faulted || !ReferenceEquals(_registeredOwner, owner) ||
                !IsPlainHint(owner._payload)) return;
            owner._hintViewModel = vm;
            vm.Text = owner._payload.ControlHint.Text;
        }

        private static bool HasSamePresentation(CoopCampaignMapTooltipPayload previous, CoopCampaignMapTooltipPayload next)
        {
            if (previous == null || previous.EntityId != next.EntityId || previous.IsExtended != next.IsExtended ||
                previous.Kind != next.Kind || previous.Mode != next.Mode || previous.Rows.Count != next.Rows.Count) return false;
            if (next.Kind == CoopCampaignMapTooltipKind.BarRundown)
            {
                var a = previous.Rundown; var b = next.Rundown;
                if (a == null || b == null || a.IsActive != b.IsActive || a.IsExtended != b.IsExtended ||
                    a.TitleText != b.TitleText || a.ExpectedChangeText != b.ExpectedChangeText || a.ExtendText != b.ExtendText ||
                    a.ValueCategorizationAsInt != b.ValueCategorizationAsInt || a.CurrentExpectedChange != b.CurrentExpectedChange ||
                    a.Lines.Count != b.Lines.Count) return false;
                for (int i = 0; i < a.Lines.Count; i++)
                    if (a.Lines[i].Name != b.Lines[i].Name || a.Lines[i].Value != b.Lines[i].Value ||
                        a.Lines[i].ValueAsString != b.Lines[i].ValueAsString) return false;
            }
            var previousRows = previous.Rows;
            var nextRows = next.Rows;
            if (next.Kind == CoopCampaignMapTooltipKind.BarControl)
            {
                if (previous.ControlHint == null || next.ControlHint == null ||
                    previous.ControlHint.Text != next.ControlHint.Text ||
                    previous.ControlHint.Rows.Count != next.ControlHint.Rows.Count) return false;
                previousRows = previous.ControlHint.Rows;
                nextRows = next.ControlHint.Rows;
            }
            for (int i = 0; i < previousRows.Count; i++)
            {
                var left = previousRows[i]; var right = nextRows[i];
                if (left.Definition != right.Definition || left.Value != right.Value || left.TextHeight != right.TextHeight ||
                    left.PropertyModifier != right.PropertyModifier || left.Red != right.Red || left.Green != right.Green ||
                    left.Blue != right.Blue || left.Alpha != right.Alpha) return false;
            }
            return true;
        }

        private void OnShow(Type type, object[] args)
        {
            _ownsVisible = (type == typeof(ReplicaTooltip) || type == typeof(ReplicaRundownTooltip) || type == typeof(ReplicaHintTooltip)) && args != null && args.Length == 1 && ReferenceEquals(args[0], this);
            if (!_ownsVisible) { _viewModel = null; _rundownViewModel = null; _hintViewModel = null; _payload = null; }
        }
        private void OnHide() { _ownsVisible = false; _viewModel = null; _rundownViewModel = null; _hintViewModel = null; _payload = null; }
        private void HideOwned()
        {
            bool owned = _ownsVisible;
            OnHide();
            if (owned) InformationManager.HideTooltip();
        }
        public void Dispose()
        {
            if (_disposed) return;
            Clear(); _disposed = true;
            InformationManager.OnShowTooltip -= OnShow;
            InformationManager.OnHideTooltip -= OnHide;
            if (ReferenceEquals(_registeredOwner, this))
            {
                // Never remove a registration replaced by another component.
                if (InformationManager.RegisteredTypes.TryGetValue(typeof(ReplicaTooltip), out var mapping) &&
                    Equals(mapping.OnRefreshData, Renderer)) InformationManager.UnregisterTooltip<ReplicaTooltip>();
                if (InformationManager.RegisteredTypes.TryGetValue(typeof(ReplicaRundownTooltip), out var rundownMapping) &&
                    Equals(rundownMapping.OnRefreshData, RundownRenderer)) InformationManager.UnregisterTooltip<ReplicaRundownTooltip>();
                _registeredOwner = null;
                if (InformationManager.RegisteredTypes.TryGetValue(typeof(ReplicaHintTooltip), out var hintMapping) &&
                    Equals(hintMapping.OnRefreshData, HintRenderer)) InformationManager.UnregisterTooltip<ReplicaHintTooltip>();
            }
        }
    }
}
