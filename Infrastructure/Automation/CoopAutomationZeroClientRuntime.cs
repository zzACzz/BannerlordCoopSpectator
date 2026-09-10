using System;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.Infrastructure.Automation
{
    internal static class CoopAutomationZeroClientRuntime
    {
        public static bool CanAdvancePreBattle(Mission mission)
        {
            // Keep native reads and peer enumeration behind the explicit, admitted profile.
            if (!CoopAutomationSpawnSmokeBridge.IsRequested ||
                !CoopAutomationSpawnSmokeBridge.MatchesMission(mission) ||
                !string.IsNullOrEmpty(CoopAutomationSpawnSmokeBridge.Failure))
                return false;

            try
            {
                if (mission == null || !ReferenceEquals(mission, Mission.Current) || !GameNetwork.IsSessionActive)
                    return false;
                var snapshot = BattleSnapshotRuntimeState.GetCurrent();
                var scenario = snapshot?.ScenarioContext;
                bool snapshotMatches = snapshot != null &&
                    snapshot.CampaignId == CoopAutomationSpawnSmokeContract.CampaignId &&
                    snapshot.BattleId == CoopAutomationSpawnSmokeContract.BattleId &&
                    snapshot.BattleInstanceId == CoopAutomationSpawnSmokeContract.BattleInstanceId &&
                    snapshot.MultiplayerScene == CoopAutomationSpawnSmokeContract.Scene &&
                    scenario != null && CoopAutomationSpawnSmokeContract.IsFirstFieldScenario(
                        scenario.ScenarioKind, scenario.CampaignBattleType, scenario.IsSiegeBattle) &&
                    snapshot.Sides != null && snapshot.Sides.Count == 2 &&
                    snapshot.Sides[0]?.Troops != null && snapshot.Sides[0].Troops.Count > 0 &&
                    snapshot.Sides[1]?.Troops != null && snapshot.Sides[1].Troops.Count > 0;

                return CoopAutomationSpawnSmokeBridge.CanUseZeroClientPreBattle(
                    mission, mission.SceneName, GameNetwork.IsServer && GameNetwork.IsDedicatedServer,
                    mission.CurrentState == Mission.State.Continuing && !mission.MissionEnded,
                    mission.Mode.ToString(), CoopBattlePhaseRuntimeState.GetPhase().ToString(),
                    HasNoConnectedClients(), snapshotMatches);
            }
            catch (Exception ex)
            {
                CoopAutomationSpawnSmokeBridge.Fail("ZeroClientAdmissionReadFailed:" + ex.GetType().Name);
                return false;
            }
        }

        public static bool HasNoConnectedClients()
        {
            if (!CoopAutomationSpawnSmokeBridge.IsRequested || !CoopAutomationSpawnSmokeBridge.IsActive)
                return false;
            var peers = GameNetwork.NetworkPeers;
            if (peers == null) return false; // Unknown is not evidence of zero clients.
            foreach (var peer in peers)
                if (peer != null && !peer.IsServerPeer && peer.IsConnectionActive)
                    return false; // Includes a connected client that has not synchronized yet.
            return true;
        }
    }
}
