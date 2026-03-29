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
                    bool found = false;
                    foreach (var b in behaviors)
                    {
                        if (b != null && b.GetType().Name == name) { found = true; break; }
                    }
                    ModLogger.Info("MissionBehaviorDiagnostic: GetMissionBehavior<" + name + "> = " + (found ? "OK" : "NULL"));
                }
            }
            catch (Exception ex)
            {
                ModLogger.Info("MissionBehaviorDiagnostic: " + ex.Message);
            }
            ModLogger.Info("MissionBehaviorDiagnostic AfterStart EXIT");
        }
    }
}
