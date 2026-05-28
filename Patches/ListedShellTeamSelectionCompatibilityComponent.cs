using System;
using CoopSpectator.Infrastructure;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.Patches
{
    internal sealed class ListedShellTeamSelectionCompatibilityComponent : MultiplayerTeamSelectComponent
    {
        private static string _lastInitializedMissionKey = string.Empty;

        public override void OnBehaviorInitialize()
        {
            base.OnBehaviorInitialize();

            string missionKey =
                (Mission?.SceneName ?? Mission.Current?.SceneName ?? "unknown") +
                "|" +
                (GameNetwork.IsServer ? "server" : "client");
            if (string.Equals(_lastInitializedMissionKey, missionKey, StringComparison.Ordinal))
                return;

            _lastInitializedMissionKey = missionKey;
            ModLogger.Info(
                "ListedShellTeamSelectionCompatibilityComponent: installed passive team-selection compatibility shell. " +
                "Mission=" + (Mission?.SceneName ?? Mission.Current?.SceneName ?? "unknown") +
                " Side=" + (GameNetwork.IsServer ? "server" : "client") + ".");
        }

        protected override void AddRemoveMessageHandlers(GameNetwork.NetworkMessageHandlerRegistererContainer registerer)
        {
            // Wrapped listed shell no longer accepts native TeamChange requests.
            // Side/team authority is bridged explicitly from coop-owned runtime state.
        }

        public override void AfterStart()
        {
            // Skip native OnTeamChanged subscription and client auto-select callbacks.
            // They would reintroduce vanilla team/culture/troop authority into the listed shell.
        }
    }
}
