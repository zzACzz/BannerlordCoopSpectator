using System;
using CoopSpectator.Infrastructure;
using CoopSpectator.MissionBehaviors;
using TaleWorlds.Core;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.GauntletUI;
using TaleWorlds.Library;
using TaleWorlds.ScreenSystem;

namespace CoopSpectator.UI
{
    public sealed partial class CoopCampaignMapPrototypeMissionView
    {
        private GauntletLayer _autoBattleLayer;
        private GauntletMovieIdentifier _autoBattleMovie;
        private CoopCampaignAftermathVM _autoBattleVm;
        private CoopCampaignAutoBattleHints _autoBattleHints;
        private CoopCampaignAutoBattlePresentation _appliedAutoBattle;
        private string _autoBattleWindow, _autoBattleFailedWindow;
        private bool HasAutoBattlePresentation => CoopCampaignMapPrototypeNetworkController.CurrentClientMenu?.AutoBattle != null &&
            CoopCampaignMapPrototypeNetworkController.IsClientMenuHostFresh;
        private void TickAutoBattle(bool modal)
        {
            var state = CoopCampaignMapPrototypeNetworkController.CurrentClientMenu?.AutoBattle;
            if (state == null || !HasAutoBattlePresentation || !_mapSceneReadyToRender || MissionScreen == null ||
                CoopCampaignMapPrototypeNetworkController.CurrentClientConversation?.IsActive == true)
            { ReleaseAutoBattle(); _autoBattleFailedWindow = null; return; }
            if (state.WindowId == _autoBattleFailedWindow) return;
            if (modal) { _autoBattleHints?.Suspend(); if (_autoBattleLayer != null) ScreenManager.SetSuspendLayer(_autoBattleLayer, true); return; }
            try
            {
                if (_autoBattleWindow != state.WindowId) ReleaseAutoBattle();
                if (_autoBattleLayer == null)
                {
                    _autoBattleVm = new CoopCampaignAftermathVM();
                    _autoBattleVm.Apply(state.Root);
                    _autoBattleHints = new CoopCampaignAutoBattleHints(MBInformationManager.ShowHint, MBInformationManager.HideInformations,
                        rows => InformationManager.ShowTooltip(typeof(System.Collections.Generic.List<TaleWorlds.Core.ViewModelCollection.Information.TooltipProperty>), rows));
                    _autoBattleVm.BindLocalHandlers(_autoBattleHints.Command, null);
                    // The binding adapter contains no native scoreboard commands,
                    // simulator, callback owner or setters. Local scrolling only.
                    _autoBattleLayer = new GauntletLayer("CoopAutoBattle", ViewOrderPriority + 6, false) { IsFocusLayer = false };
                    _autoBattleLayer.InputRestrictions.SetInputRestrictions(true, InputUsageMask.Mouse);
                    MissionScreen.AddLayer(_autoBattleLayer);
                    _autoBattleMovie = _autoBattleLayer.LoadMovie("SPScoreboard", _autoBattleVm);
                    _autoBattleWindow = state.WindowId; _appliedAutoBattle = state;
                }
                else if (!ReferenceEquals(state, _appliedAutoBattle))
                { _autoBattleVm.Apply(state.Root); _appliedAutoBattle = state; }
                _autoBattleHints?.Tick();
                ScreenManager.SetSuspendLayer(_autoBattleLayer, false);
            }
            catch (Exception ex)
            {
                ReleaseAutoBattle(); _autoBattleFailedWindow = state.WindowId;
                ModLogger.Info("Replica auto-battle presentation unavailable: " + ex.GetType().Name);
            }
        }
        private void ReleaseAutoBattle()
        {
            _autoBattleHints?.Dispose(); _autoBattleHints = null;
            if (_autoBattleLayer != null)
            {
                if (_autoBattleMovie != null) _autoBattleLayer.ReleaseMovie(_autoBattleMovie);
                MissionScreen?.RemoveLayer(_autoBattleLayer);
            }
            _autoBattleVm?.OnFinalize(); _autoBattleVm = null; _autoBattleMovie = null; _autoBattleLayer = null;
            _autoBattleWindow = null; _appliedAutoBattle = null;
        }
    }
}
