using System;
using System.Reflection;
using CoopSpectator.Infrastructure;
using SandBox.View.Map;
using TaleWorlds.CampaignSystem.GameState;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.ScreenSystem;

namespace CoopSpectator.Campaign
{
    internal static class CoopCampaignAutoBattleCapture
    {
        private static object _owner, _view, _campaign;
        private static string _session, _window;
        private static readonly FieldInfo SimulationView = typeof(MapScreen).GetField("_battleSimulationView", BindingFlags.Instance | BindingFlags.NonPublic);
        internal static void Reset() { _owner = _view = _campaign = null; _session = _window = null; CoopCampaignAutoBattleRewardCapture.Reset(); }
        // No scoreboard/simulation construction, Tick, Initialize, events or commands.
        internal static bool TryCapture(string session, out CoopCampaignAutoBattlePresentation state, out string reason)
        {
            state = null; reason = null;
            try
            {
                var campaign = TaleWorlds.CampaignSystem.Campaign.Current;
                var screen = MapScreen.Instance;
                if (campaign == null || screen == null || !ReferenceEquals(ScreenManager.TopScreen, screen) ||
                    !(Game.Current?.GameStateManager?.ActiveState is MapState) || TaleWorlds.MountAndBlade.Mission.Current != null ||
                    campaign.ConversationManager?.IsConversationInProgress == true) { Reset(); return true; }
                // The Gauntlet override derives from MapView, not the placeholder
                // BattleSimulationMapView. Read the exact owner retained by MapScreen.
                if (SimulationView == null) { reason = "native-simulation-owner-layout-unavailable"; Reset(); return false; }
                var view = SimulationView.GetValue(screen) as MapView;
                if (view == null) { Reset(); return true; }
                if (view.GetType().FullName != "SandBox.GauntletUI.Map.GauntletMapBattleSimulationView" || view.Layer == null)
                { reason = "native-simulation-view-not-ready"; Reset(); return false; }
                var field = view.GetType().GetField("_dataSource", BindingFlags.Instance | BindingFlags.NonPublic);
                var source = field?.GetValue(view) as ViewModel;
                if (source?.GetType().FullName != "SandBox.ViewModelCollection.SPScoreboardVM")
                { reason = "native-simulation-source-unavailable"; Reset(); return false; }
                if (!ReferenceEquals(_owner, source) || !ReferenceEquals(_view, view) || !ReferenceEquals(_campaign, campaign) || _session != session)
                { CoopCampaignAutoBattleRewardCapture.Reset(); _owner = source; _view = view; _campaign = campaign; _session = session; _window = Guid.NewGuid().ToString("N"); }
                var root = CoopCampaignAftermathCapture.CapturePreparedWithPolicy(source, IsAllowed);
                CoopCampaignAutoBattleRewardCapture.Append(source, root);
                if (!ReferenceEquals(screen, MapScreen.Instance) || !ReferenceEquals(ScreenManager.TopScreen, screen) ||
                    !ReferenceEquals(view, SimulationView.GetValue(screen)) || !ReferenceEquals(source, field.GetValue(view)) ||
                    !ReferenceEquals(campaign, TaleWorlds.CampaignSystem.Campaign.Current))
                { reason = "native-simulation-owner-changed"; Reset(); return false; }
                var copy = new CoopCampaignAutoBattlePresentation { WindowId = _window, Root = root };
                if (!copy.IsValid()) { reason = "native-simulation-presentation-invalid"; Reset(); return false; }
                state = copy; return true;
            }
            catch (Exception ex) { reason = "native-simulation-capture-" + ex.GetType().Name; Reset(); return false; }
        }
        private static bool IsAllowed(Type type)
        {
            switch (type.FullName)
            {
                case "SandBox.ViewModelCollection.SPScoreboardVM":
                case "TaleWorlds.MountAndBlade.ViewModelCollection.Scoreboard.SPScoreboardSideVM":
                case "TaleWorlds.MountAndBlade.ViewModelCollection.Scoreboard.SPScoreboardPartyVM":
                case "TaleWorlds.MountAndBlade.ViewModelCollection.Scoreboard.SPScoreboardUnitVM":
                case "TaleWorlds.MountAndBlade.ViewModelCollection.Scoreboard.SPScoreboardShipVM":
                case "TaleWorlds.MountAndBlade.ViewModelCollection.Scoreboard.SPScoreboardStatsVM":
                case "TaleWorlds.MountAndBlade.ViewModelCollection.Scoreboard.SPScoreboardSkillItemVM":
                case "TaleWorlds.MountAndBlade.ViewModelCollection.Scoreboard.SPScoreboardSortControllerVM":
                case "TaleWorlds.MountAndBlade.ViewModelCollection.PowerLevelComparer":
                case "TaleWorlds.MountAndBlade.ViewModelCollection.Input.InputKeyItemVM": return true;
                default: return (type.Namespace ?? string.Empty).StartsWith("TaleWorlds.Core.ViewModelCollection", StringComparison.Ordinal) ||
                    type.Namespace == "TaleWorlds.Library";
            }
        }
    }
}
