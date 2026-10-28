using CoopSpectator.Infrastructure;
using CoopSpectator.MissionBehaviors;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.GauntletUI;
using TaleWorlds.Library;
using TaleWorlds.ScreenSystem;
using TaleWorlds.TwoDimension;

namespace CoopSpectator.UI
{
    public sealed partial class CoopCampaignMapPrototypeMissionView
    {
        private GauntletLayer _aftermathLayer;
        private GauntletMovieIdentifier _aftermathMovie;
        private CoopCampaignAftermathVM _aftermathVm;
        private SpriteCategory _aftermathSprites;
        private string _aftermathWindow;
        private CoopCampaignAftermathPresentation _appliedAftermath;
        private string _aftermathFailedWindow;
        private ICoopCampaignAftermathInspection _aftermathInspection;

        private bool HasAftermathPresentation => CoopCampaignMapPrototypeNetworkController.CurrentClientMenu?.Aftermath != null &&
            CoopCampaignMapPrototypeNetworkController.IsClientMenuHostFresh;

        private void TickAftermath(bool localModal)
        {
            var window = CoopCampaignMapPrototypeNetworkController.CurrentClientMenu?.Aftermath?.WindowId;
            if (window != null && window == _aftermathFailedWindow) return;
            try { TickAftermathCore(localModal); }
            catch (System.Exception ex)
            {
                ReleaseAftermath(); _aftermathFailedWindow = window;
                ModLogger.Info("Replica aftermath presentation unavailable: " + ex.GetType().Name);
            }
        }

        private void TickAftermathCore(bool localModal)
        {
            var state = CoopCampaignMapPrototypeNetworkController.CurrentClientMenu?.Aftermath;
            bool show = state != null && HasAftermathPresentation && _mapSceneReadyToRender && MissionScreen != null &&
                CoopCampaignMapPrototypeNetworkController.CurrentClientConversation?.IsActive != true;
            if (!show) { ReleaseAftermath(); _aftermathFailedWindow = null; return; }
            if (localModal)
            { _aftermathInspection?.Suspend(); if (_aftermathLayer != null) ScreenManager.SetSuspendLayer(_aftermathLayer, true); return; }
            if (_aftermathWindow != state.WindowId) ReleaseAftermath();
            if (_aftermathLayer == null)
            {
                _aftermathSprites = UIResourceManager.LoadSpriteCategory(state.Kind == CoopCampaignAftermathKind.Prisoners ? "ui_partyscreen" : "ui_inventory");
                _aftermathVm = new CoopCampaignAftermathVM();
                _aftermathVm.Apply(state.Root);
                if (state.Kind == CoopCampaignAftermathKind.Loot)
                    _aftermathInspection = new CoopCampaignLootInspection(_aftermathVm, state.WindowId);
                else
                    _aftermathInspection = new CoopCampaignPrisonerInspection(_aftermathVm, state.WindowId);
                _aftermathLayer = new GauntletLayer("CoopCampaignAftermath", ViewOrderPriority + 5, false) { IsFocusLayer = false };
                _aftermathLayer.InputRestrictions.SetInputRestrictions(true, InputUsageMask.Mouse);
                MissionScreen.AddLayer(_aftermathLayer);
                _aftermathMovie = _aftermathLayer.LoadMovie(state.Kind == CoopCampaignAftermathKind.Prisoners ?
                    "CoopCampaignPrisoners" : "CoopCampaignLoot", _aftermathVm);
                _aftermathWindow = state.WindowId;
                _appliedAftermath = state;
            }
            else if (!ReferenceEquals(state, _appliedAftermath))
            { _aftermathVm.Apply(state.Root); _appliedAftermath = state; }
            ScreenManager.SetSuspendLayer(_aftermathLayer, false);
            _aftermathInspection?.Tick(state);
        }

        private void ReleaseAftermath()
        {
            _aftermathInspection?.Dispose(); _aftermathInspection = null;
            if (_aftermathLayer != null)
            {
                if (_aftermathMovie != null) _aftermathLayer.ReleaseMovie(_aftermathMovie);
                MissionScreen?.RemoveLayer(_aftermathLayer);
            }
            _aftermathVm?.OnFinalize(); _aftermathSprites?.Unload();
            _aftermathLayer = null; _aftermathMovie = null; _aftermathVm = null;
            _aftermathSprites = null; _aftermathWindow = null; _appliedAftermath = null;
        }
    }
}
