using System;
using System.Collections.Generic;
using CoopSpectator.Infrastructure;
using CoopSpectator.MissionBehaviors;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.GameMode
{
    /// <summary>Логує наявність компонентів, які очікує Gauntlet UI (MissionOptionsComponent, MissionBoundaryCrossingHandler, MultiplayerPollComponent, MissionLobbyEquipmentNetworkComponent).</summary>
    public sealed class MissionBehaviorDiagnostic : MissionLogic
    {
        private static readonly string[] CriticalTypeNames = { "MissionOptionsComponent", "MissionBoundaryCrossingHandler", "MultiplayerPollComponent", "MissionLobbyEquipmentNetworkComponent" };
        private static readonly string[] BattleMapUiParityTypeNames =
        {
            "MissionAgentLabelUIHandler",
            "MissionAgentLabelView",
            "MissionFormationTargetSelectionHandler",
            "MissionFormationMarkerUIHandler",
            "MissionGauntletFormationMarker",
            "MultiplayerMissionOrderUIHandler",
            "MissionGauntletMultiplayerOrderUIHandler",
            "OrderTroopPlacer"
        };
        private bool _loggedBattleMapClientObserverFallback;

        public override void AfterStart()
        {
            base.AfterStart();
            if (!ExperimentalFeatures.EnableVerboseDiagnostics)
                return;

            ModLogger.Verbose("MissionBehaviorDiagnostic AfterStart ENTER");
            Mission mission = Mission;
            if (mission == null) return;
            try
            {
                List<MissionBehavior> behaviors = mission.MissionBehaviors;
                if (behaviors == null) { ModLogger.Verbose("MissionBehaviorDiagnostic: MissionBehaviors is null."); return; }
                foreach (string name in CriticalTypeNames)
                {
                    bool found = ContainsBehavior(behaviors, name);
                    ModLogger.Verbose("MissionBehaviorDiagnostic: GetMissionBehavior<" + name + "> = " + (found ? "OK" : "NULL"));
                }

                foreach (string name in BattleMapUiParityTypeNames)
                {
                    bool found = ContainsBehavior(behaviors, name);
                    ModLogger.Verbose("MissionBehaviorDiagnostic: UIParity<" + name + "> = " + (found ? "OK" : "NULL"));
                }

                List<string> relevantBehaviorTypes = new List<string>();
                foreach (MissionBehavior behavior in behaviors)
                {
                    if (behavior == null)
                        continue;

                    string typeName = behavior.GetType().Name ?? string.Empty;
                    if (typeName.IndexOf("Label", StringComparison.OrdinalIgnoreCase) >= 0
                        || typeName.IndexOf("Formation", StringComparison.OrdinalIgnoreCase) >= 0
                        || typeName.IndexOf("Order", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        relevantBehaviorTypes.Add(typeName);
                    }
                }

                ModLogger.Verbose(
                    "MissionBehaviorDiagnostic: relevant UI behavior types = " +
                    (relevantBehaviorTypes.Count > 0 ? string.Join(", ", relevantBehaviorTypes) : "(none)"));
            }
            catch (Exception ex)
            {
                ModLogger.Info("MissionBehaviorDiagnostic failed: " + ex.Message);
            }
            ModLogger.Verbose("MissionBehaviorDiagnostic AfterStart EXIT");
        }

        public override void OnMissionTick(float dt)
        {
            base.OnMissionTick(dt);

            Mission mission = Mission;
            if (mission == null || GameNetwork.IsServer || !MissionMultiplayerCoopBattleMode.IsBattleMapSceneName(mission.SceneName))
                return;

            if (!_loggedBattleMapClientObserverFallback)
            {
                _loggedBattleMapClientObserverFallback = true;
                ModLogger.Verbose(
                    "MissionBehaviorDiagnostic: running battle-map client exact visual observer fallback because CoopMissionClientLogic is not injected in crash-isolation stack.");
            }

            CoopMissionSpawnLogic.TryRunClientExactCampaignVisualObserver(mission);
            CoopMissionSpawnLogic.TryRunClientGlobalCaptainAgentStatRefresh(mission);
        }

        private static bool ContainsBehavior(List<MissionBehavior> behaviors, string typeName)
        {
            if (behaviors == null || string.IsNullOrEmpty(typeName))
                return false;

            foreach (MissionBehavior behavior in behaviors)
            {
                if (behavior != null && string.Equals(behavior.GetType().Name, typeName, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }
    }
}
