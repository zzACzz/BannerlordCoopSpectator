using System;
using CoopSpectator.Infrastructure;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.Patches
{
    internal sealed class ListedShellEquipmentCompatibilityComponent : MissionLobbyEquipmentNetworkComponent
    {
        private static string _lastInitializedMissionKey = string.Empty;

        public override void OnBehaviorInitialize()
        {
            GameNetwork.AddNetworkHandler(this);

            string missionKey =
                (Mission?.SceneName ?? Mission.Current?.SceneName ?? "unknown") +
                "|" +
                (GameNetwork.IsServer ? "server" : "client");
            if (string.Equals(_lastInitializedMissionKey, missionKey, StringComparison.Ordinal))
                return;

            _lastInitializedMissionKey = missionKey;
            ModLogger.Info(
                "ListedShellEquipmentCompatibilityComponent: installed passive equipment compatibility shell. " +
                "Mission=" + (Mission?.SceneName ?? Mission.Current?.SceneName ?? "unknown") +
                " Side=" + (GameNetwork.IsServer ? "server" : "client") + ".");
        }

        protected override void AddRemoveMessageHandlers(GameNetwork.NetworkMessageHandlerRegistererContainer registerer)
        {
            // Wrapped listed shell no longer uses native loadout/perk/troop-index network bootstrap.
            // Keep only the MissionNetwork shell so SpawningBehaviorBase can still resolve the expected type.
        }

        protected override void OnEndMission()
        {
        }
    }
}
