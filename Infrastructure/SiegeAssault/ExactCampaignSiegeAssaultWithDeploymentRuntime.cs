using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CoopSpectator.Network.Messages;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.Missions.Handlers;
using TaleWorlds.MountAndBlade.Source.Missions;

namespace CoopSpectator.Infrastructure
{
    internal static class ExactCampaignSiegeAssaultWithDeploymentRuntime
    {
        private static readonly FieldInfo DefaultMissionDeploymentPlanTeamDeploymentPlansField =
            typeof(DefaultMissionDeploymentPlan).GetField("_teamDeploymentPlans", BindingFlags.Instance | BindingFlags.NonPublic);

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

        public static bool TryPrepareDeploymentPlanContract(
            Mission mission,
            IMissionTroopSupplier[] suppliers,
            BattleSideEnum playerSide,
            out string diagnostics)
        {
            diagnostics = "mission-null";
            if (mission == null)
                return false;

            if (!mission.GetDeploymentPlan<DefaultMissionDeploymentPlan>(out DefaultMissionDeploymentPlan deploymentPlan) ||
                deploymentPlan == null)
            {
                diagnostics = "deployment-plan-null";
                return false;
            }

            if (!TryEnsureTeamDeploymentPlans(mission, deploymentPlan, out string teamPlanDiagnostics))
            {
                diagnostics = "team-plans={" + teamPlanDiagnostics + "}";
                return false;
            }

            List<Team> battleTeams = mission.Teams?
                .Where(team => team != null && team.Side != BattleSideEnum.None)
                .ToList() ?? new List<Team>();
            if (battleTeams.Count <= 0)
            {
                diagnostics = "team-plans={" + teamPlanDiagnostics + "} BattleTeams=0";
                return false;
            }

            var troopCountsByTeam = battleTeams.ToDictionary(
                team => team,
                _ => new Dictionary<FormationClass, (int Foot, int Mounted)>());

            int totalTroops = 0;
            int unresolvedTeamAssignments = 0;
            int fallbackTeamAssignments = 0;
            int skippedOrigins = 0;

            for (int sideIndex = 0; sideIndex < 2; sideIndex++)
            {
                BattleSideEnum side = (BattleSideEnum)sideIndex;
                IMissionTroopSupplier supplier =
                    suppliers != null && sideIndex >= 0 && sideIndex < suppliers.Length
                        ? suppliers[sideIndex]
                        : null;
                if (supplier == null)
                    continue;

                Team fallbackTeam = battleTeams.FirstOrDefault(team => team.Side == side);
                bool isPlayerSide = side == playerSide;
                IEnumerable<IAgentOriginBase> troops;
                try
                {
                    troops = supplier.GetAllTroops() ?? Array.Empty<IAgentOriginBase>();
                }
                catch (Exception ex)
                {
                    diagnostics =
                        "team-plans={" + teamPlanDiagnostics + "} " +
                        "supplier-read-failed Side=" + side +
                        " Message=" + ex.GetType().Name + ":" + ex.Message;
                    return false;
                }

                foreach (IAgentOriginBase troopOrigin in troops)
                {
                    BasicCharacterObject troop = troopOrigin?.Troop;
                    if (troop == null)
                    {
                        skippedOrigins++;
                        continue;
                    }

                    Team troopTeam = null;
                    try
                    {
                        troopTeam = Mission.GetAgentTeam(troopOrigin, isPlayerSide);
                    }
                    catch
                    {
                    }

                    if (troopTeam == null || troopTeam.Side != side)
                    {
                        troopTeam = fallbackTeam;
                        if (troopTeam != null)
                        {
                            fallbackTeamAssignments++;
                        }
                        else
                        {
                            unresolvedTeamAssignments++;
                            continue;
                        }
                    }

                    FormationClass formationClass = ResolveDeploymentFormationClass(mission, side, troop);
                    if (!troopCountsByTeam.TryGetValue(troopTeam, out Dictionary<FormationClass, (int Foot, int Mounted)> formationCounts))
                    {
                        formationCounts = new Dictionary<FormationClass, (int Foot, int Mounted)>();
                        troopCountsByTeam[troopTeam] = formationCounts;
                    }

                    formationCounts.TryGetValue(formationClass, out (int Foot, int Mounted) currentCount);
                    if (troop.HasMount())
                        formationCounts[formationClass] = (currentCount.Foot, currentCount.Mounted + 1);
                    else
                        formationCounts[formationClass] = (currentCount.Foot + 1, currentCount.Mounted);

                    totalTroops++;
                }
            }

            try
            {
                deploymentPlan.ClearAll();
            }
            catch (Exception ex)
            {
                diagnostics =
                    "team-plans={" + teamPlanDiagnostics + "} " +
                    "clear-all-failed Message=" + ex.GetType().Name + ":" + ex.Message;
                return false;
            }

            int plansMade = 0;
            foreach (Team team in battleTeams)
            {
                try
                {
                    deploymentPlan.SetSpawnWithHorses(team, false);
                    if (troopCountsByTeam.TryGetValue(team, out Dictionary<FormationClass, (int Foot, int Mounted)> formationCounts))
                    {
                        foreach (KeyValuePair<FormationClass, (int Foot, int Mounted)> formationCount in formationCounts.OrderBy(pair => (int)pair.Key))
                        {
                            if (formationCount.Value.Foot <= 0 && formationCount.Value.Mounted <= 0)
                                continue;

                            deploymentPlan.AddTroops(team, formationCount.Key, formationCount.Value.Foot, formationCount.Value.Mounted);
                            deploymentPlan.AddTroops(team, formationCount.Key, formationCount.Value.Foot, formationCount.Value.Mounted, isReinforcement: true);
                        }
                    }

                    deploymentPlan.MakeDeploymentPlan(team);
                    deploymentPlan.MakeReinforcementDeploymentPlan(team);
                    plansMade++;
                }
                catch (Exception ex)
                {
                    diagnostics =
                        "team-plans={" + teamPlanDiagnostics + "} " +
                        "team-plan-build-failed Team=#" + team.TeamIndex + "/" + team.Side +
                        " Message=" + ex.GetType().Name + ":" + ex.Message;
                    return false;
                }
            }

            diagnostics =
                "TeamPlans={" + teamPlanDiagnostics + "} " +
                "BattleTeams=" + battleTeams.Count +
                " PlansMade=" + plansMade +
                " TotalTroops=" + totalTroops +
                " FallbackTeamAssignments=" + fallbackTeamAssignments +
                " UnresolvedTeamAssignments=" + unresolvedTeamAssignments +
                " SkippedOrigins=" + skippedOrigins +
                " TeamCounts=[" + BuildTeamCountSummary(troopCountsByTeam) + "]";
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

        private static bool TryEnsureTeamDeploymentPlans(
            Mission mission,
            DefaultMissionDeploymentPlan deploymentPlan,
            out string diagnostics)
        {
            diagnostics = "mission-null";
            if (mission == null || deploymentPlan == null)
                return false;

            if (DefaultMissionDeploymentPlanTeamDeploymentPlansField == null)
            {
                diagnostics = "team-deployment-plan-field-missing";
                return false;
            }

            object teamPlans = DefaultMissionDeploymentPlanTeamDeploymentPlansField.GetValue(deploymentPlan);
            if (!(teamPlans is System.Collections.IEnumerable enumerable))
            {
                diagnostics = "team-deployment-plans-not-enumerable";
                return false;
            }

            var existingTeams = new HashSet<Team>();
            foreach (object entry in enumerable)
            {
                Team existingTeam = TryReadMember(entry, "team") as Team ?? TryReadMember(entry, "Item1") as Team;
                if (existingTeam != null)
                    existingTeams.Add(existingTeam);
            }

            List<Team> battleTeams = mission.Teams?
                .Where(team => team != null && team.Side != BattleSideEnum.None)
                .ToList() ?? new List<Team>();
            List<Team> missingTeams = battleTeams
                .Where(team => !existingTeams.Contains(team))
                .ToList();

            if (missingTeams.Count <= 0)
            {
                diagnostics = "already-ready Existing=" + existingTeams.Count + " BattleTeams=" + battleTeams.Count;
                return true;
            }

            MethodInfo addMethod = teamPlans.GetType().GetMethod("Add", BindingFlags.Instance | BindingFlags.Public);
            if (addMethod == null)
            {
                diagnostics = "team-deployment-plan-add-method-missing";
                return false;
            }

            foreach (Team missingTeam in missingTeams)
            {
                addMethod.Invoke(
                    teamPlans,
                    new object[] { (missingTeam, new DefaultTeamDeploymentPlan(mission, missingTeam)) });
            }

            diagnostics =
                "added-missing-team-plans MissingTeams=[" +
                string.Join(", ", missingTeams.Select(team => "#" + team.TeamIndex + "/" + team.Side)) +
                "] ExistingBefore=" + existingTeams.Count +
                " ExistingAfter=" + (existingTeams.Count + missingTeams.Count);
            return true;
        }

        private static FormationClass ResolveDeploymentFormationClass(
            Mission mission,
            BattleSideEnum side,
            BasicCharacterObject troop)
        {
            FormationClass formationClass = troop?.DefaultFormationClass ?? FormationClass.Infantry;
            try
            {
                if (mission != null && troop != null)
                    formationClass = mission.GetAgentTroopClass(side, troop);
            }
            catch
            {
            }

            if (formationClass == FormationClass.NumberOfRegularFormations ||
                formationClass == FormationClass.NumberOfAllFormations)
            {
                formationClass = troop?.DefaultFormationClass ?? FormationClass.Infantry;
            }

            formationClass = formationClass.FallbackClass();
            int formationIndex = (int)formationClass;
            if (formationIndex < 0 || formationIndex >= 11)
                return FormationClass.Infantry;

            return formationClass;
        }

        private static object TryReadMember(object instance, string memberName)
        {
            if (instance == null || string.IsNullOrWhiteSpace(memberName))
                return null;

            BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            Type type = instance.GetType();

            PropertyInfo property = type.GetProperty(memberName, flags);
            if (property != null)
                return property.GetValue(instance, null);

            FieldInfo field = type.GetField(memberName, flags);
            if (field != null)
                return field.GetValue(instance);

            return null;
        }

        private static string BuildTeamCountSummary(
            IDictionary<Team, Dictionary<FormationClass, (int Foot, int Mounted)>> troopCountsByTeam)
        {
            if (troopCountsByTeam == null || troopCountsByTeam.Count <= 0)
                return "none";

            return string.Join(
                "; ",
                troopCountsByTeam.Select(teamEntry =>
                {
                    int teamTotal = teamEntry.Value?.Sum(pair => pair.Value.Foot + pair.Value.Mounted) ?? 0;
                    string formationSummary = teamEntry.Value == null || teamEntry.Value.Count <= 0
                        ? "none"
                        : string.Join(
                            ", ",
                            teamEntry.Value
                                .OrderBy(pair => (int)pair.Key)
                                .Select(pair => pair.Key + "=" + (pair.Value.Foot + pair.Value.Mounted) + "(F" + pair.Value.Foot + "/M" + pair.Value.Mounted + ")"));

                    return
                        "#" + teamEntry.Key.TeamIndex + "/" + teamEntry.Key.Side +
                        ":Total=" + teamTotal +
                        " Formations={" + formationSummary + "}";
                }));
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
