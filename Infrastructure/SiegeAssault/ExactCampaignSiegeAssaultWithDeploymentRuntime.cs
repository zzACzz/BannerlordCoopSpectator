using System;
using System.Collections.Generic;
using CoopSpectator.Network.Messages;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.Missions.Handlers;
using TaleWorlds.MountAndBlade.Source.Missions;

namespace CoopSpectator.Infrastructure
{
    internal static class ExactCampaignSiegeAssaultWithDeploymentRuntime
    {
        public static bool IsSiegeAssaultScenario(BattleScenarioContextMessage scenarioContext)
        {
            string siegeSubtype = scenarioContext?.SiegeContext?.SiegeSubtype ?? string.Empty;
            string missionShell = scenarioContext?.SiegeContext?.MissionShell ?? string.Empty;
            return scenarioContext?.IsSiegeBattle == true &&
                   string.Equals(siegeSubtype, "SiegeAssault", StringComparison.OrdinalIgnoreCase) &&
                   CampaignMissionShellRuntimeState.IsWithDeploymentMissionShell(missionShell);
        }

        public static bool TryEnsureMissionBehaviorContract(Mission mission, out string diagnostics)
        {
            diagnostics = "mission-null";
            if (mission == null)
                return false;

            if (!TryEnsureMissionBehaviorAvailable(
                    mission,
                    mission.GetMissionBehavior<MissionSiegeEnginesLogic>(),
                    () => new MissionSiegeEnginesLogic(
                        new List<MissionSiegeWeapon>(),
                        new List<MissionSiegeWeapon>()),
                    "MissionSiegeEnginesLogic",
                    out string siegeEnginesDiagnostics))
            {
                diagnostics = "MissionSiegeEnginesLogic={" + siegeEnginesDiagnostics + "}";
                return false;
            }

            if (!TryEnsureMissionBehaviorAvailable(
                    mission,
                    mission.GetMissionBehavior<SiegeDeploymentHandler>(),
                    () => new SiegeDeploymentHandler(false),
                    "SiegeDeploymentHandler",
                    out string deploymentHandlerDiagnostics))
            {
                diagnostics =
                    "MissionSiegeEnginesLogic={" + siegeEnginesDiagnostics + "} " +
                    "SiegeDeploymentHandler={" + deploymentHandlerDiagnostics + "}";
                return false;
            }

            if (!TryEnsureMissionBehaviorAvailable(
                    mission,
                    mission.GetMissionBehavior<SiegeDeploymentMissionController>(),
                    () => new SiegeDeploymentMissionController(false),
                    "SiegeDeploymentMissionController",
                    out string deploymentControllerDiagnostics))
            {
                diagnostics =
                    "MissionSiegeEnginesLogic={" + siegeEnginesDiagnostics + "} " +
                    "SiegeDeploymentHandler={" + deploymentHandlerDiagnostics + "} " +
                    "SiegeDeploymentMissionController={" + deploymentControllerDiagnostics + "}";
                return false;
            }

            diagnostics =
                "MissionSiegeEnginesLogic={" + siegeEnginesDiagnostics + "} " +
                "SiegeDeploymentHandler={" + deploymentHandlerDiagnostics + "} " +
                "SiegeDeploymentMissionController={" + deploymentControllerDiagnostics + "}";
            return true;
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
                    "SpawnMode=BestEffortWithDeployment " +
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

        private static bool TryEnsureMissionBehaviorAvailable<TBehavior>(
            Mission mission,
            TBehavior existingBehavior,
            Func<TBehavior> behaviorFactory,
            string behaviorName,
            out string diagnostics)
            where TBehavior : MissionBehavior
        {
            diagnostics = "mission-null";
            if (mission == null)
                return false;

            if (existingBehavior != null)
            {
                diagnostics = "Existing=True Created=False";
                return true;
            }

            if (behaviorFactory == null)
            {
                diagnostics = "Existing=False Created=False Reason=factory-null";
                return false;
            }

            try
            {
                TBehavior behavior = behaviorFactory();
                if (behavior == null)
                {
                    diagnostics = "Existing=False Created=False Reason=factory-returned-null";
                    return false;
                }

                mission.AddMissionBehavior(behavior);
                behavior.OnBehaviorInitialize();
                behavior.AfterStart();
                diagnostics = "Existing=False Created=True RuntimeType=" + behaviorName;
                return true;
            }
            catch (Exception ex)
            {
                diagnostics =
                    "Existing=False Created=False Reason=" +
                    ex.GetType().Name + ":" + ex.Message;
                return false;
            }
        }
    }
}
