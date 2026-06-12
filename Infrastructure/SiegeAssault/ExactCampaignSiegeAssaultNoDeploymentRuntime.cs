using System;
using CoopSpectator.Network.Messages;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.Source.Missions;

namespace CoopSpectator.Infrastructure
{
    internal static class ExactCampaignSiegeAssaultNoDeploymentRuntime
    {
        public static bool IsSiegeAssaultScenario(BattleScenarioContextMessage scenarioContext)
        {
            string siegeSubtype = scenarioContext?.SiegeContext?.SiegeSubtype ?? string.Empty;
            string missionShell = scenarioContext?.SiegeContext?.MissionShell ?? string.Empty;
            return scenarioContext?.IsSiegeBattle == true &&
                   string.Equals(siegeSubtype, "SiegeAssault", StringComparison.OrdinalIgnoreCase) &&
                   !CampaignMissionShellRuntimeState.IsWithDeploymentMissionShell(missionShell);
        }

        public static bool TryPrepareLateBattleSpawnLogic(Mission mission, out string diagnostics)
        {
            diagnostics = "mission-null";
            if (mission == null)
                return false;

            BattleSpawnLogic battleSpawnLogic = mission.GetMissionBehavior<BattleSpawnLogic>();
            if (battleSpawnLogic == null)
            {
                diagnostics = "battle-spawn-logic-missing";
                return false;
            }

            try
            {
                battleSpawnLogic.OnPreMissionTick(0f);
                diagnostics = "battle-set-prepared";
                return true;
            }
            catch (Exception ex)
            {
                diagnostics = "battle-spawn-logic-faulted " + ex.GetType().Name + ":" + ex.Message;
                return false;
            }
        }

        public static bool TryApplyNativeLikeSpawnHandlerContract(
            DefaultBattleMissionAgentSpawnLogic spawnLogic,
            int defenderTotal,
            int attackerTotal,
            int defenderInitial,
            int attackerInitial,
            in MissionSpawnSettings spawnSettings,
            out string diagnostics)
        {
            diagnostics = "spawn-logic-null";
            if (spawnLogic == null)
                return false;

            try
            {
                spawnLogic.SetSpawnHorses(BattleSideEnum.Defender, false);
                spawnLogic.SetSpawnHorses(BattleSideEnum.Attacker, false);
                spawnLogic.InitWithSinglePhase(
                    defenderTotal,
                    attackerTotal,
                    defenderInitial,
                    attackerInitial,
                    spawnDefenders: defenderTotal > 0,
                    spawnAttackers: attackerTotal > 0,
                    in spawnSettings);
                diagnostics =
                    "SpawnHorses={Defender=False Attacker=False} " +
                    "SinglePhaseInitialized=True " +
                    "DefenderTotal=" + defenderTotal +
                    " AttackerTotal=" + attackerTotal +
                    " DefenderInitial=" + defenderInitial +
                    " AttackerInitial=" + attackerInitial;
                return true;
            }
            catch (Exception ex)
            {
                diagnostics =
                    "spawn-handler-faulted " +
                    ex.GetType().Name + ":" + ex.Message +
                    " DefenderTotal=" + defenderTotal +
                    " AttackerTotal=" + attackerTotal +
                    " DefenderInitial=" + defenderInitial +
                    " AttackerInitial=" + attackerInitial;
                return false;
            }
        }
    }
}
