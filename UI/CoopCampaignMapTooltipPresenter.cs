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
        private static readonly Action<PropertyBasedTooltipVM, object[]> Renderer = Refresh;
        private static CoopCampaignMapTooltipPresenter _registeredOwner;
        private PropertyBasedTooltipVM _viewModel;
        private CoopCampaignMapTooltipPayload _payload;
        private bool _ownsVisible, _creating, _disposed, _faulted;
        private float _extendTime;

        public CoopCampaignMapTooltipPresenter()
        {
            // An older mission must release its registration before replacement.
            _registeredOwner?.Dispose();
            InformationManager.RegisterTooltip<ReplicaTooltip, PropertyBasedTooltipVM>(Renderer, "PropertyBasedTooltip");
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
                _payload = payload;
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
            foreach (var row in payload.Rows)
                vm.TooltipPropertyList.Add(new TooltipProperty(row.Definition, row.Value, row.TextHeight,
                    new Color(row.Red, row.Green, row.Blue, row.Alpha), false,
                    (TooltipProperty.TooltipPropertyFlags)row.PropertyModifier));
            vm.IsActive = vm.TooltipPropertyList.Count > 0;
        }

        private static bool HasSamePresentation(CoopCampaignMapTooltipPayload previous, CoopCampaignMapTooltipPayload next)
        {
            if (previous == null || previous.EntityId != next.EntityId || previous.IsExtended != next.IsExtended ||
                previous.Mode != next.Mode || previous.Rows.Count != next.Rows.Count) return false;
            for (int i = 0; i < previous.Rows.Count; i++)
            {
                var left = previous.Rows[i]; var right = next.Rows[i];
                if (left.Definition != right.Definition || left.Value != right.Value || left.TextHeight != right.TextHeight ||
                    left.PropertyModifier != right.PropertyModifier || left.Red != right.Red || left.Green != right.Green ||
                    left.Blue != right.Blue || left.Alpha != right.Alpha) return false;
            }
            return true;
        }

        private void OnShow(Type type, object[] args)
        {
            _ownsVisible = type == typeof(ReplicaTooltip) && args != null && args.Length == 1 && ReferenceEquals(args[0], this);
            if (!_ownsVisible) { _viewModel = null; _payload = null; }
        }
        private void OnHide() { _ownsVisible = false; _viewModel = null; _payload = null; }
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
                _registeredOwner = null;
            }
        }
    }
}
