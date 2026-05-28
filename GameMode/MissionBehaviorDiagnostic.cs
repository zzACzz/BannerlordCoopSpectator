using System;
using System.Collections.Generic;
using CoopSpectator.Infrastructure;
using CoopSpectator.MissionBehaviors;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.GameMode
{
    /// <summary>Логує наявність компонентів, які ще очікує coop/native client shell (MissionOptionsComponent, MissionBoundaryCrossingHandler, MultiplayerPollComponent).</summary>
    public sealed class MissionBehaviorDiagnostic : MissionLogic
    {
        private static readonly string[] CriticalTypeNames = { "MissionOptionsComponent", "MissionBoundaryCrossingHandler", "MultiplayerPollComponent" };
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
            ModLogger.Info("MissionBehaviorDiagnostic AfterStart ENTER");
            base.AfterStart();
            Mission mission = Mission;
            if (mission == null) return;
            try
            {
                List<MissionBehavior> behaviors = mission.MissionBehaviors;
                if (behaviors == null) { ModLogger.Info("MissionBehaviorDiagnostic: MissionBehaviors is null."); return; }
                foreach (string name in CriticalTypeNames)
                {
                    bool found = ContainsBehavior(behaviors, name);
                    ModLogger.Info("MissionBehaviorDiagnostic: GetMissionBehavior<" + name + "> = " + (found ? "OK" : "NULL"));
                }

                foreach (string name in BattleMapUiParityTypeNames)
                {
                    bool found = ContainsBehavior(behaviors, name);
                    ModLogger.Info("MissionBehaviorDiagnostic: UIParity<" + name + "> = " + (found ? "OK" : "NULL"));
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

                ModLogger.Info(
                    "MissionBehaviorDiagnostic: relevant UI behavior types = " +
                    (relevantBehaviorTypes.Count > 0 ? string.Join(", ", relevantBehaviorTypes) : "(none)"));
            }
            catch (Exception ex)
            {
                ModLogger.Info("MissionBehaviorDiagnostic: " + ex.Message);
            }
            ModLogger.Info("MissionBehaviorDiagnostic AfterStart EXIT");
        }

        public override void OnMissionTick(float dt)
        {
            base.OnMissionTick(dt);

            Mission mission = Mission;
            if (mission == null || GameNetwork.IsServer || !MissionMultiplayerCoopBattleMode.IsBattleMapSceneName(mission.SceneName))
                return;

            List<MissionBehavior> behaviors = mission.MissionBehaviors;
            if (ContainsBehavior(behaviors, nameof(CoopMissionClientLogic)))
                return;

            if (!_loggedBattleMapClientObserverFallback)
            {
                _loggedBattleMapClientObserverFallback = true;
                ModLogger.Info(
                    "MissionBehaviorDiagnostic: running battle-map client exact visual observer fallback.");
            }

            CoopMissionSpawnLogic.TryRunClientExactCampaignVisualObserver(mission);
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
