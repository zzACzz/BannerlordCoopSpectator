using System;
using CoopSpectator.Infrastructure;
using CoopSpectator.MissionBehaviors;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.GauntletUI;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade.View;
using TaleWorlds.ScreenSystem;
using TaleWorlds.TwoDimension;

namespace CoopSpectator.UI
{
    public sealed partial class CoopCampaignMapPrototypeMissionView
    {
        private GauntletLayer _mapBarLayer;
        private GauntletMovieIdentifier _mapBarMovie;
        private SpriteCategory _mapBarSprites;
        private CoopCampaignMapBarVM _mapBarVm;
        private CoopCampaignMapBarPresentation _appliedMapBar;
        private bool _mapBarVisible, _mapBarLoadFailed;
        private string _hoveredMapBarHint;
        private void OnMapBarHintHover(string token, bool begin)
        { if (begin) _hoveredMapBarHint = token; else if (_hoveredMapBarHint == token) _hoveredMapBarHint = null; }

        private bool IsPointerOverMapBar() => _mapBarVisible && _mapBarLayer != null && _mapBarLayer.HitTest();
        private void TickMapBar()
        {
            var state = CoopCampaignMapPrototypeNetworkController.CurrentClientMapBar;
            bool modal = ScreenManager.FocusedLayer != null && !ReferenceEquals(ScreenManager.FocusedLayer, _replicaInfoLayer);
            bool visible = _mapSceneReadyToRender && MissionScreen != null && state != null && state.IsEnabled &&
                CoopCampaignMapPrototypeNetworkController.IsClientMenuHostFresh &&
                CoopCampaignMapPrototypeNetworkController.CurrentClientConversation?.IsActive != true && !modal;
            if (!visible)
            {
                _mapBarVisible = false;
                _mapBarVm?.ClearHintHover(); _hoveredMapBarHint = null;
                if (_mapBarLayer != null) ScreenManager.SetSuspendLayer(_mapBarLayer, true);
                return;
            }
            if (_mapBarLoadFailed) return;
            try
            {
                if (_mapBarLayer == null)
                {
                    _mapBarVm = new CoopCampaignMapBarVM(FollowHostCamera, OnMapBarHintHover);
                    _mapBarVm.Apply(state);
                    _mapBarSprites = UIResourceManager.LoadSpriteCategory("ui_mapbar");
                    _mapBarLayer = new GauntletLayer("CoopMapBar", ViewOrderPriority + 5, false) { IsFocusLayer = false };
                    _mapBarLayer.InputRestrictions.SetInputRestrictions(true, InputUsageMask.Mouse);
                    MissionScreen.AddLayer(_mapBarLayer);
                    _mapBarMovie = _mapBarLayer.LoadMovie("MapBar", _mapBarVm);
                    _appliedMapBar = state;
                }
                else if (!ReferenceEquals(state, _appliedMapBar))
                { _mapBarVm.Apply(state); _appliedMapBar = state; }
                ScreenManager.SetSuspendLayer(_mapBarLayer, false);
                _mapBarVisible = true;
            }
            catch (Exception ex)
            {
                ReleaseMapBar(); _mapBarLoadFailed = true;
                ModLogger.Info("Replica map bar unavailable: " + ex.GetType().Name);
            }
        }
        private void ReleaseMapBar()
        {
            _mapBarVisible = false;
            _mapBarVm?.ClearHintHover(); _hoveredMapBarHint = null;
            if (_mapBarLayer != null)
            {
                if (_mapBarMovie != null) _mapBarLayer.ReleaseMovie(_mapBarMovie);
                MissionScreen?.RemoveLayer(_mapBarLayer);
            }
            _mapBarVm?.OnFinalize();
            _mapBarSprites?.Unload();
            _mapBarVm = null; _mapBarMovie = null; _mapBarLayer = null; _mapBarSprites = null; _appliedMapBar = null;
        }
    }
}
