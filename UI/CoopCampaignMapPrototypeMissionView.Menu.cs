using System;
using CoopSpectator.Infrastructure;
using CoopSpectator.MissionBehaviors;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.GauntletUI;
using TaleWorlds.Library;
using TaleWorlds.ScreenSystem;

namespace CoopSpectator.UI
{
    public sealed partial class CoopCampaignMapPrototypeMissionView
    {
        private GauntletLayer _campaignMenuLayer;
        private GauntletMovieIdentifier _campaignMenuMovie;
        private CoopCampaignMapMenuVM _campaignMenuVm;
        private GauntletMovieIdentifier _encounterMovie;
        private CoopCampaignMapEncounterVM _encounterVm;
        private GauntletMovieIdentifier _settlementMenuMovie;
        private CoopCampaignSettlementMenuVM _settlementMenuVm;
        private bool _settlementResourcesReady;
        private GauntletMovieIdentifier _siegeMovie;
        private CoopCampaignMapSiegeOverlayVM _siegeVm;
        private float _siegeLocalCameraDistance = float.NaN;
        private CoopCampaignMapMenuPresentation _appliedCampaignMenu;
        private bool _campaignMenuLoadFailed;
        private bool _campaignMenuVisible;
        private bool _encounterResourcesReady;
        private CoopDetachedPortraitResources _encounterResources;
        private string _hoveredPlunderId;
        private readonly CoopCampaignMapSiegeProjectilePlayback _siegeProjectilePlayback = new CoopCampaignMapSiegeProjectilePlayback();

        private void OnPlunderTooltipHover(string id, bool hovered)
        {
            if (hovered) _hoveredPlunderId = id;
            else if (_hoveredPlunderId == id) _hoveredPlunderId = null;
        }

        private bool IsPointerOverCampaignMenu() => HasAutoBattlePresentation || HasAftermathPresentation || IsPointerOverMapBar() || (_campaignMenuVisible &&
            _campaignMenuLayer != null && _campaignMenuLayer.HitTest());

        private void TickCampaignMenu()
        {
            var state = CoopCampaignMapPrototypeNetworkController.CurrentClientMenu;
            bool modal = ScreenManager.FocusedLayer != null &&
                !ReferenceEquals(ScreenManager.FocusedLayer, _replicaInfoLayer);
            TickAftermath(modal);
            TickAutoBattle(modal);
            bool visible = _mapSceneReadyToRender && MissionScreen != null &&
                CoopCampaignMapPrototypeNetworkController.IsClientMenuHostFresh &&
                CoopCampaignMapPrototypeNetworkController.CurrentClientConversation?.IsActive != true &&
                state != null && state.Aftermath == null && state.AutoBattle == null && !modal;
            if (!visible)
            {
                _siegeProjectilePlayback.Clear();
                ClearSiegeDecals();
                ClearSiegeMachines();
                ClearSiegeProjectiles();
                _campaignMenuVm?.ClearTooltipHover();
                _encounterVm?.ClearTooltipHover();
                _hoveredPlunderId = null;
                _campaignMenuVisible = false;
                if (_campaignMenuLayer != null) ScreenManager.SetSuspendLayer(_campaignMenuLayer, true);
                return;
            }
            if (_campaignMenuLoadFailed) return;
            try
            {
                if (_campaignMenuLayer == null)
                {
                    _campaignMenuVm = new CoopCampaignMapMenuVM(OnPlunderTooltipHover);
                    _campaignMenuVm.Apply(state);
                    _campaignMenuLayer = new GauntletLayer("CoopCampaignMenu", ViewOrderPriority + 4, false)
                    { IsFocusLayer = false };
                    // Native hover dispatch requires mouse admission even for read-only menus.
                    // Command VMs remain inert; keyboard focus stays with the replica layer.
                    _campaignMenuLayer.InputRestrictions.SetInputRestrictions(true, InputUsageMask.Mouse);
                    MissionScreen.AddLayer(_campaignMenuLayer);
                    _campaignMenuMovie = _campaignMenuLayer.LoadMovie("CoopCampaignMenu", _campaignMenuVm);
                    ApplyEncounterMenu(state.Encounter);
                    ApplySettlementMenu(state.SettlementOverlay);
                    ApplySiegeMenu(state.SiegeOverlay);
                    _appliedCampaignMenu = state;
                }
                else if (!ReferenceEquals(state, _appliedCampaignMenu))
                {
                    _campaignMenuVm.Apply(state);
                    ApplyEncounterMenu(state.Encounter);
                    ApplySettlementMenu(state.SettlementOverlay);
                    ApplySiegeMenu(state.SiegeOverlay);
                    _appliedCampaignMenu = state;
                }
                else if (_encounterResourcesReady != CoopCampaignMapPrototypeNetworkController.ClientPortraitResources.IsReady(state.Encounter) ||
                    !ReferenceEquals(_encounterResources, CoopCampaignMapPrototypeNetworkController.ClientPortraitResources))
                    ApplyEncounterMenu(state.Encounter);
                if (_settlementResourcesReady != CoopCampaignMapPrototypeNetworkController.ClientPortraitResources.IsReady(state.SettlementOverlay))
                    ApplySettlementMenu(state.SettlementOverlay);
                if (_siegeVm != null && state.SiegeOverlay != null)
                {
                    if (_cameraPolicy.IsFollowingHost || !IsFinite(_siegeLocalCameraDistance))
                        _siegeLocalCameraDistance = state.SiegeOverlay.CameraDistance;
                    _siegeVm.Update(_camera, _siegeLocalCameraDistance <= 20f);
                }
                ScreenManager.SetSuspendLayer(_campaignMenuLayer, false);
                ApplySiegeDecals(state.SiegeOverlay?.Decals);
                var projectiles = _siegeProjectilePlayback.Sample(state.SiegeOverlay,
                    System.Diagnostics.Stopwatch.GetTimestamp() / (double)System.Diagnostics.Stopwatch.Frequency);
                ApplySiegeMachines(_siegeProjectilePlayback.Machines ?? state.SiegeOverlay?.Machines);
                ApplySiegeProjectiles(projectiles);
                _campaignMenuVisible = true;
            }
            catch (Exception ex)
            {
                ReleaseCampaignMenu();
                _campaignMenuLoadFailed = true;
                ModLogger.Info("Replica campaign menu unavailable: " + ex.GetType().Name);
            }
        }

        private void ApplyEncounterMenu(CoopCampaignMapEncounterPresentation state)
        {
            _encounterResources = CoopCampaignMapPrototypeNetworkController.ClientPortraitResources;
            _encounterResourcesReady = _encounterResources.IsReady(state);
            if (state == null)
            {
                if (_encounterMovie != null) _campaignMenuLayer.ReleaseMovie(_encounterMovie);
                _encounterVm?.OnFinalize(); _encounterVm = null; _encounterMovie = null;
                return;
            }
            if (_encounterVm == null)
            {
                _encounterVm = new CoopCampaignMapEncounterVM(OnPlunderTooltipHover);
                _encounterVm.Apply(state, _encounterResourcesReady ? _encounterResources : null);
                _encounterMovie = _campaignMenuLayer.LoadMovie("EncounterOverlay", _encounterVm);
            }
            else _encounterVm.Apply(state, _encounterResourcesReady ? _encounterResources : null);
        }

        private void ReleaseCampaignMenu()
        {
            ReleaseAftermath();
            ReleaseAutoBattle();
            _siegeProjectilePlayback.Clear();
            ClearSiegeDecals();
            ClearSiegeMachines();
            ClearSiegeProjectiles();
            _hoveredPlunderId = null;
            _campaignMenuVisible = false;
            if (_campaignMenuLayer != null)
            {
                if (_siegeMovie != null) _campaignMenuLayer.ReleaseMovie(_siegeMovie);
                if (_encounterMovie != null) _campaignMenuLayer.ReleaseMovie(_encounterMovie);
                if (_settlementMenuMovie != null) _campaignMenuLayer.ReleaseMovie(_settlementMenuMovie);
                if (_campaignMenuMovie != null) _campaignMenuLayer.ReleaseMovie(_campaignMenuMovie);
                MissionScreen?.RemoveLayer(_campaignMenuLayer);
            }
            _campaignMenuVm?.OnFinalize();
            _siegeVm?.OnFinalize(); _siegeVm = null; _siegeMovie = null;
            _siegeLocalCameraDistance = float.NaN;
            _encounterVm?.OnFinalize(); _encounterVm = null; _encounterMovie = null;
            _settlementMenuVm?.OnFinalize(); _settlementMenuVm = null; _settlementMenuMovie = null;
            _campaignMenuLayer = null; _campaignMenuMovie = null;
            _campaignMenuVm = null; _appliedCampaignMenu = null;
        }

        private void ApplySettlementMenu(CoopCampaignSettlementMenuPresentation state)
        {
            var resources = CoopCampaignMapPrototypeNetworkController.ClientPortraitResources;
            _settlementResourcesReady = resources.IsReady(state);
            if (state == null)
            {
                if (_settlementMenuMovie != null) _campaignMenuLayer.ReleaseMovie(_settlementMenuMovie);
                _settlementMenuVm?.OnFinalize(); _settlementMenuVm = null; _settlementMenuMovie = null;
                return;
            }
            if (_settlementMenuVm == null)
            {
                _settlementMenuVm = new CoopCampaignSettlementMenuVM(OnPlunderTooltipHover);
                _settlementMenuVm.Apply(state, _settlementResourcesReady ? resources : null);
                _settlementMenuMovie = _campaignMenuLayer.LoadMovie("SettlementOverlay", _settlementMenuVm);
            }
            else _settlementMenuVm.Apply(state, _settlementResourcesReady ? resources : null);
        }

        private void ApplySiegeMenu(CoopCampaignMapSiegeOverlay state)
        {
            if (state == null)
            {
                if (_siegeMovie != null) _campaignMenuLayer.ReleaseMovie(_siegeMovie);
                _siegeVm?.OnFinalize(); _siegeVm = null; _siegeMovie = null;
                _siegeLocalCameraDistance = float.NaN;
                return;
            }
            if (_siegeVm == null)
            {
                _siegeVm = new CoopCampaignMapSiegeOverlayVM();
                _siegeVm.Apply(state);
                _siegeLocalCameraDistance = state.CameraDistance;
                _siegeMovie = _campaignMenuLayer.LoadMovie("MapSiegeOverlay", _siegeVm);
            }
            else _siegeVm.Apply(state);
        }
    }
}
