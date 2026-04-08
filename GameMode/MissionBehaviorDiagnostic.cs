using System;
using System.Collections.Generic;
using CoopSpectator.Infrastructure;
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
