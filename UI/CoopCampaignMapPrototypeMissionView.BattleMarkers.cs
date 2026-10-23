using System;
using CoopSpectator.Infrastructure;
using CoopSpectator.MissionBehaviors;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.GauntletUI;
using TaleWorlds.ScreenSystem;

namespace CoopSpectator.UI
{
    public sealed partial class CoopCampaignMapPrototypeMissionView
    {
        private GauntletLayer _battleMarkerLayer;
        private GauntletMovieIdentifier _battleMarkerMovie;
        private CoopCampaignMapBattleMarkersVM _battleMarkerVm;
        private CoopCampaignMapBattleMarker[] _appliedBattleMarkers;
        private bool _battleMarkerLoadFailed;

        private void TickBattleMarkers()
        {
            var state = CoopCampaignMapPrototypeNetworkController.CurrentClientBattleMarkers;
            bool visible = _mapSceneReadyToRender && _camera != null && MissionScreen != null &&
                CoopCampaignMapPrototypeNetworkController.IsClientBattleMarkersHostFresh &&
                CoopCampaignMapPrototypeNetworkController.CurrentClientConversation?.IsActive != true &&
                state != null && state.Length > 0;
            if (!visible)
            {
                if (_battleMarkerLayer != null) ScreenManager.SetSuspendLayer(_battleMarkerLayer, true);
                return;
            }
            if (_battleMarkerLoadFailed) return;
            try
            {
                if (_battleMarkerLayer == null)
                {
                    _battleMarkerVm = new CoopCampaignMapBattleMarkersVM();
                    _battleMarkerLayer = new GauntletLayer("CoopBattleMarkers", ViewOrderPriority + 2, false)
                    { IsFocusLayer = false };
                    MissionScreen.AddLayer(_battleMarkerLayer);
                    _battleMarkerMovie = _battleMarkerLayer.LoadMovie("MapEventVisuals", _battleMarkerVm);
                }
                if (!ReferenceEquals(state, _appliedBattleMarkers))
                { _battleMarkerVm.Apply(state); _appliedBattleMarkers = state; }
                _battleMarkerVm.Update(_camera);
                ScreenManager.SetSuspendLayer(_battleMarkerLayer, false);
            }
            catch (Exception ex)
            {
                ReleaseBattleMarkers(); _battleMarkerLoadFailed = true;
                ModLogger.Info("Replica battle markers unavailable: " + ex.GetType().Name);
            }
        }

        private void ReleaseBattleMarkers()
        {
            if (_battleMarkerLayer != null)
            {
                if (_battleMarkerMovie != null) _battleMarkerLayer.ReleaseMovie(_battleMarkerMovie);
                MissionScreen?.RemoveLayer(_battleMarkerLayer);
            }
            _battleMarkerVm?.OnFinalize();
            _battleMarkerVm = null; _battleMarkerMovie = null; _battleMarkerLayer = null;
            _appliedBattleMarkers = null;
        }
    }
}
