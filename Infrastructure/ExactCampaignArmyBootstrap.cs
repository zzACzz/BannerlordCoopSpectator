using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.Source.Missions;
using TaleWorlds.ObjectSystem;

namespace CoopSpectator.Infrastructure
{
    public static class ExactCampaignArmyBootstrap
    {
        private static Mission _activeMission;
        private static MissionAgentSpawnLogic _activeSpawnLogic;
        private static BattleSideEnum _activePlayerSide = BattleSideEnum.None;
        private static bool _reinforcementsEnabled;
        private static DateTime _nextDeferredLogUtc = DateTime.MinValue;
        private static Mission _spawnLogicInitSideOverrideMission;
        private static BattleSideEnum _spawnLogicInitSideOverride = BattleSideEnum.None;
        private static int _spawnLogicInitSideOverrideDepth;
        private static readonly FieldInfo TeamSideBackingField =
            typeof(Team).GetField("<Side>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo DefaultMissionDeploymentPlanTeamDeploymentPlansField =
            typeof(DefaultMissionDeploymentPlan).GetField("_teamDeploymentPlans", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly PropertyInfo MissionInitializerRecordProperty =
            typeof(Mission).GetProperty("InitializerRecord", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo MissionAgentSpawnLogicMissionSidesField =
            typeof(MissionAgentSpawnLogic).GetField("_missionSides", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo MissionAgentSpawnLogicPhasesField =
            typeof(MissionAgentSpawnLogic).GetField("_phases", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo MissionAgentSpawnLogicNumberOfTroopsInTotalField =
            typeof(MissionAgentSpawnLogic).GetField("_numberOfTroopsInTotal", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo MissionAgentSpawnLogicDeploymentPlanField =
            typeof(MissionAgentSpawnLogic).GetField("_deploymentPlan", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo MissionAgentSpawnLogicPlayerSideField =
            typeof(MissionAgentSpawnLogic).GetField("_playerSide", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo DefaultMissionDeploymentPlanFormationSceneSpawnEntriesField =
            typeof(DefaultMissionDeploymentPlan).GetField("_formationSceneSpawnEntries", BindingFlags.Instance | BindingFlags.NonPublic);

        private readonly struct TeamSideOverrideState
        {
            public TeamSideOverrideState(
                Team team,
                BattleSideEnum originalSide,
                bool addedTemporaryDeploymentPlan,
                int temporaryDeploymentPlanIndex)
            {
                Team = team;
                OriginalSide = originalSide;
                AddedTemporaryDeploymentPlan = addedTemporaryDeploymentPlan;
                TemporaryDeploymentPlanIndex = temporaryDeploymentPlanIndex;
            }

            public Team Team { get; }

            public BattleSideEnum OriginalSide { get; }

            public bool AddedTemporaryDeploymentPlan { get; }

            public int TemporaryDeploymentPlanIndex { get; }
        }

        public static bool IsActive(Mission mission)
        {
            return mission != null &&
                   ReferenceEquals(_activeMission, mission) &&
                   _activeSpawnLogic != null;
        }

        public static bool TryGetSpawnLogicInitTeamSideOverride(
            Mission mission,
            BattleSideEnum currentSide,
            out BattleSideEnum overrideSide)
        {
            if (_spawnLogicInitSideOverrideDepth > 0 &&
                mission != null &&
                ReferenceEquals(_spawnLogicInitSideOverrideMission, mission) &&
                currentSide == BattleSideEnum.None &&
                _spawnLogicInitSideOverride != BattleSideEnum.None)
            {
                overrideSide = _spawnLogicInitSideOverride;
                return true;
            }

            overrideSide = BattleSideEnum.None;
            return false;
        }

        public static void ResetForMission(Mission mission)
        {
            if (ReferenceEquals(_activeMission, mission))
                return;

            _activeMission = mission;
            _activeSpawnLogic = null;
            _activePlayerSide = BattleSideEnum.None;
            _reinforcementsEnabled = false;
            _nextDeferredLogUtc = DateTime.MinValue;
        }

        public static bool TryInitialize(
            Mission mission,
            BattleSideEnum playerSide,
            string source,
            out string reason)
        {
            string initializationStep = "enter";
            reason = string.Empty;
            try
            {
                initializationStep = "validate-mission";
                if (mission == null)
                {
                    reason = "mission-null";
                    return false;
                }

                initializationStep = "reset-runtime";
                ResetForMission(mission);
                if (_activeSpawnLogic != null)
                    return true;

                initializationStep = "validate-feature";
                if (!ExperimentalFeatures.EnableExactCampaignNativeArmyBootstrap)
                {
                    reason = "feature-disabled";
                    return false;
                }

                initializationStep = "validate-scene";
                string sceneName = mission.SceneName ?? string.Empty;
                if (!SceneRuntimeClassifier.IsCampaignBattleScene(sceneName))
                {
                    reason = "scene-not-exact-campaign";
                    return false;
                }

                initializationStep = "validate-player-side";
                if (playerSide == BattleSideEnum.None)
                {
                    reason = "player-side-none";
                    return false;
                }

                initializationStep = "validate-player-teams";
                if (mission.PlayerTeam == null || mission.PlayerEnemyTeam == null)
                {
                    reason = "player-teams-not-ready";
                    return false;
                }

                initializationStep = "validate-player-team-side";
                if (mission.PlayerTeam.Side != playerSide)
                {
                    reason =
                        "player-team-side-mismatch MissionPlayerTeam=" + mission.PlayerTeam.Side +
                        " RequestedPlayerSide=" + playerSide;
                    return false;
                }

                initializationStep = "build-suppliers";
                if (!TryBuildSuppliers(playerSide, out IMissionTroopSupplier[] suppliers, out int defenderTotal, out int attackerTotal, out string supplierDiagnostics))
                {
                    reason = supplierDiagnostics ?? "supplier-build-failed";
                    return false;
                }

                initializationStep = "resolve-battle-spawn-logic";
                MissionBehavior existingBattleSpawnLogic = mission.GetMissionBehavior<BattleSpawnLogic>();
                if (existingBattleSpawnLogic == null)
                {
                    initializationStep = "create-battle-spawn-logic";
                    var battleSpawnLogic = new BattleSpawnLogic(BattleSpawnLogic.BattleTag);
                    mission.AddMissionBehavior(battleSpawnLogic);
                    initializationStep = "battle-spawn-logic-onbehaviorinitialize";
                    battleSpawnLogic.OnBehaviorInitialize();
                    initializationStep = "battle-spawn-logic-afterstart";
                    battleSpawnLogic.AfterStart();
                }

                initializationStep = "resolve-agent-spawn-logic";
                MissionAgentSpawnLogic spawnLogic = mission.GetMissionBehavior<MissionAgentSpawnLogic>();
                if (spawnLogic == null)
                {
                    initializationStep = "create-agent-spawn-logic";
                    spawnLogic = new MissionAgentSpawnLogic(suppliers, playerSide, Mission.BattleSizeType.Battle);
                    initializationStep = "add-agent-spawn-logic";
                    mission.AddMissionBehavior(spawnLogic);
                    initializationStep = "agent-spawn-logic-onbehaviorinitialize";
                    spawnLogic.OnBehaviorInitialize();
                    initializationStep = "agent-spawn-logic-afterstart";
                    spawnLogic.AfterStart();
                }

                initializationStep = "build-spawn-settings";
                MissionSpawnSettings spawnSettings = new MissionSpawnSettings(
                    MissionSpawnSettings.InitialSpawnMethod.FreeAllocation,
                    MissionSpawnSettings.ReinforcementTimingMethod.GlobalTimer,
                    MissionSpawnSettings.ReinforcementSpawnMethod.Balanced,
                    globalReinforcementInterval: 10f,
                    reinforcementBatchPercentage: 0.05f,
                    desiredReinforcementPercentage: 0.166f);
                initializationStep = "compute-initial-counts";
                ComputeInitialSpawnCounts(defenderTotal, attackerTotal, out int defenderInitial, out int attackerInitial, out int battleSizeBudget);

                initializationStep = "ensure-deployment-team-plans";
                if (!TryEnsureDeploymentPlanTeamPlans(mission, source, out string deploymentPlanDiagnostics))
                {
                    reason = deploymentPlanDiagnostics ?? "deployment-team-plan-bridge-failed";
                    return false;
                }

                initializationStep = "init-with-single-phase";
                PushSpawnLogicInitTeamSideOverride(mission, playerSide);
                List<TeamSideOverrideState> temporaryTeamSideOverrides =
                    PushInitTeamSideSanitization(mission, playerSide, source);
                try
                {
                    LogBootstrapContractSnapshot(
                        mission,
                        spawnLogic,
                        playerSide,
                        supplierDiagnostics + " DeploymentPlanBridge={" + deploymentPlanDiagnostics + "}",
                        "pre-init-with-single-phase",
                        source);
                    spawnLogic.InitWithSinglePhase(
                        defenderTotal,
                        attackerTotal,
                        defenderInitial,
                        attackerInitial,
                        spawnDefenders: defenderInitial > 0,
                        spawnAttackers: attackerInitial > 0,
                        in spawnSettings);
                }
                finally
                {
                    PopInitTeamSideSanitization(temporaryTeamSideOverrides, source);
                    PopSpawnLogicInitTeamSideOverride(mission);
                }
                initializationStep = "disable-reinforcements";
                spawnLogic.SetReinforcementsSpawnEnabled(false);

                initializationStep = "activate-runtime";
                _activeMission = mission;
                _activeSpawnLogic = spawnLogic;
                _activePlayerSide = playerSide;
                _reinforcementsEnabled = false;
                reason = "initialized";

                ModLogger.Info(
                    "ExactCampaignArmyBootstrap: initialized native-like army bootstrap on exact campaign scene. " +
                    "Scene=" + sceneName +
                    " PlayerSide=" + playerSide +
                    " DefenderTotal=" + defenderTotal +
                    " AttackerTotal=" + attackerTotal +
                    " DefenderInitial=" + defenderInitial +
                    " AttackerInitial=" + attackerInitial +
                    " BattleSizeBudget=" + battleSizeBudget +
                    " SupplierDiagnostics=" + supplierDiagnostics +
                    " Source=" + (source ?? "unknown"));
                return true;
            }
            catch (Exception ex)
            {
                string playerTeamText =
                    mission?.PlayerTeam == null
                        ? "null"
                        : mission.PlayerTeam.Side + "#" + mission.PlayerTeam.TeamIndex;
                string playerEnemyTeamText =
                    mission?.PlayerEnemyTeam == null
                        ? "null"
                        : mission.PlayerEnemyTeam.Side + "#" + mission.PlayerEnemyTeam.TeamIndex;
                reason = "exception@" + initializationStep + ":" + ex.GetType().Name + ":" + ex.Message;
                LogBootstrapContractSnapshot(
                    mission,
                    mission?.GetMissionBehavior<MissionAgentSpawnLogic>(),
                    playerSide,
                    reason,
                    "exception-" + initializationStep,
                    source);
                ModLogger.Info(
                    "ExactCampaignArmyBootstrap: initialization failed with exception. " +
                    "Scene=" + (mission?.SceneName ?? "null") +
                    " PlayerSide=" + playerSide +
                    " Step=" + initializationStep +
                    " PlayerTeam=" + playerTeamText +
                    " PlayerEnemyTeam=" + playerEnemyTeamText +
                    " Error=" + ex);
                return false;
            }
        }

        private static void LogBootstrapContractSnapshot(
            Mission mission,
            MissionAgentSpawnLogic spawnLogic,
            BattleSideEnum playerSide,
            string details,
            string stage,
            string source)
        {
            if (mission == null)
                return;

            string playerTeamText =
                mission.PlayerTeam == null
                    ? "null"
                    : mission.PlayerTeam.Side + "#" + mission.PlayerTeam.TeamIndex;
            string playerEnemyTeamText =
                mission.PlayerEnemyTeam == null
                    ? "null"
                    : mission.PlayerEnemyTeam.Side + "#" + mission.PlayerEnemyTeam.TeamIndex;

            ModLogger.Info(
                "ExactCampaignArmyBootstrap: contract snapshot. " +
                "Stage=" + stage +
                " Scene=" + (mission.SceneName ?? "null") +
                " PlayerSide=" + playerSide +
                " MissionMode=" + mission.Mode +
                " MissionTeamAIType=" + mission.MissionTeamAIType +
                " HasSpawnPath=" + mission.HasSpawnPath +
                " LiveHasSceneMapPatch=" + SafeHasSceneMapPatch(mission) +
                " ReflectedInitializerRecord={" + BuildReflectedInitializerRecordSummary(mission) + "}" +
                " PlayerTeam=" + playerTeamText +
                " PlayerEnemyTeam=" + playerEnemyTeamText +
                " BattleSize=" + MissionAgentSpawnLogic.MaxNumberOfAgentsForMission +
                " Source=" + (source ?? "unknown") +
                " Details=" + (details ?? string.Empty));

            ModLogger.Info(
                "ExactCampaignArmyBootstrap: mission teams snapshot. " +
                "Stage=" + stage +
                " Teams=[" + BuildMissionTeamsSummary(mission) + "] " +
                " Source=" + (source ?? "unknown"));

            ModLogger.Info(
                "ExactCampaignArmyBootstrap: deployment plan snapshot. " +
                "Stage=" + stage +
                " " + BuildDeploymentPlanSummary(mission) +
                " Source=" + (source ?? "unknown"));

            if (spawnLogic != null)
            {
                ModLogger.Info(
                    "ExactCampaignArmyBootstrap: spawn logic snapshot. " +
                    "Stage=" + stage +
                    " " + BuildSpawnLogicSummary(spawnLogic) +
                    " Source=" + (source ?? "unknown"));
            }

            string behaviorSummary =
                mission.MissionBehaviors == null
                    ? "null"
                    : string.Join(", ", mission.MissionBehaviors.Select(behavior => behavior?.GetType().Name ?? "null"));
            ModLogger.Info(
                "ExactCampaignArmyBootstrap: mission behaviors snapshot. " +
                "Stage=" + stage +
                " Behaviors=[" + behaviorSummary + "] " +
                " Source=" + (source ?? "unknown"));
        }

        private static string BuildMissionTeamsSummary(Mission mission)
        {
            if (mission?.Teams == null)
                return "null";

            return string.Join(
                "; ",
                mission.Teams.Select(team =>
                {
                    if (team == null)
                        return "null-team";

                    return
                        "#" + team.TeamIndex +
                        " Side=" + team.Side +
                        " IsPlayerTeam=" + team.IsPlayerTeam +
                        " IsPlayerAlly=" + team.IsPlayerAlly +
                        " HasAI=" + (team.TeamAI != null) +
                        " Formations=" + (team.FormationsIncludingSpecialAndEmpty?.Count ?? -1) +
                        " ActiveAgents=" + (team.ActiveAgents?.Count ?? -1) +
                        " QueryReady=" + (team.QuerySystem != null);
                }));
        }

        private static string BuildDeploymentPlanSummary(Mission mission)
        {
            if (mission == null)
                return "Mission=null";

            if (!mission.GetDeploymentPlan<DefaultMissionDeploymentPlan>(out DefaultMissionDeploymentPlan deploymentPlan) ||
                deploymentPlan == null)
            {
                return "DeploymentPlan=null";
            }

            var builder = new StringBuilder();
            builder.Append("DeploymentPlanType=");
            builder.Append(deploymentPlan.GetType().Name);
            builder.Append(" TeamPlans=[");
            builder.Append(BuildTeamPlanCollectionSummary(deploymentPlan));
            builder.Append("]");

            Array spawnEntries =
                DefaultMissionDeploymentPlanFormationSceneSpawnEntriesField?.GetValue(deploymentPlan) as Array;
            builder.Append(" FormationSceneSpawnEntries=");
            builder.Append(spawnEntries == null ? "null" : spawnEntries.Length.ToString());

            builder.Append(" Boundaries=");
            builder.Append(mission.Boundaries == null ? "null" : mission.Boundaries.Count.ToString());
            return builder.ToString();
        }

        private static bool SafeHasSceneMapPatch(Mission mission)
        {
            try
            {
                return mission != null && mission.HasSceneMapPatch();
            }
            catch
            {
                return false;
            }
        }

        private static string BuildReflectedInitializerRecordSummary(Mission mission)
        {
            if (mission == null || MissionInitializerRecordProperty == null)
                return "unavailable";

            try
            {
                object record = MissionInitializerRecordProperty.GetValue(mission, null);
                if (record == null)
                    return "null";

                return
                    "SceneName=" + FormatMemberValue(TryReadMember(record, "SceneName")) +
                    " SceneLevels=" + FormatMemberValue(TryReadMember(record, "SceneLevels")) +
                    " PlayingInCampaignMode=" + FormatMemberValue(TryReadMember(record, "PlayingInCampaignMode")) +
                    " SceneHasMapPatch=" + FormatMemberValue(TryReadMember(record, "SceneHasMapPatch")) +
                    " PatchCoordinates=" + FormatMemberValue(TryReadMember(record, "PatchCoordinates")) +
                    " PatchEncounterDir=" + FormatMemberValue(TryReadMember(record, "PatchEncounterDir"));
            }
            catch (Exception ex)
            {
                return "reflection-failed:" + ex.Message;
            }
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

        private static string FormatMemberValue(object value)
        {
            return value?.ToString() ?? "null";
        }

        private static string BuildTeamPlanCollectionSummary(DefaultMissionDeploymentPlan deploymentPlan)
        {
            object teamPlans = DefaultMissionDeploymentPlanTeamDeploymentPlansField?.GetValue(deploymentPlan);
            if (teamPlans == null)
                return "null";

            var enumerable = teamPlans as System.Collections.IEnumerable;
            if (enumerable == null)
                return "not-enumerable";

            var entries = new List<string>();
            foreach (object entry in enumerable)
            {
                if (entry == null)
                {
                    entries.Add("null");
                    continue;
                }

                Type entryType = entry.GetType();
                Team team = TryReadMember(entry, "team") as Team ?? TryReadMember(entry, "Item1") as Team;
                object plan = TryReadMember(entry, "plan") ?? TryReadMember(entry, "Item2");
                entries.Add(
                    "Team=" + (team == null ? "null" : "#" + team.TeamIndex + "/" + team.Side) +
                    " Plan=" + (plan == null ? "null" : plan.GetType().Name));
            }

            return string.Join(", ", entries);
        }

        private static bool TryEnsureDeploymentPlanTeamPlans(
            Mission mission,
            string source,
            out string diagnostics)
        {
            diagnostics = string.Empty;
            if (mission == null)
            {
                diagnostics = "mission-null";
                return false;
            }

            if (!mission.GetDeploymentPlan<DefaultMissionDeploymentPlan>(out DefaultMissionDeploymentPlan deploymentPlan) ||
                deploymentPlan == null)
            {
                diagnostics = "deployment-plan-null";
                return false;
            }

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

            var currentTeams = enumerable.Cast<object>().ToList();
            var existingTeams = new HashSet<Team>();
            foreach (object entry in currentTeams)
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
                diagnostics =
                    "already-ready Existing=" + existingTeams.Count +
                    " BattleTeams=" + battleTeams.Count;
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

            ModLogger.Info(
                "ExactCampaignArmyBootstrap: ensured deployment plan team plans before native bootstrap init. " +
                "Scene=" + (mission.SceneName ?? "null") +
                " " + diagnostics +
                " Source=" + (source ?? "unknown"));
            return true;
        }

        private static string BuildSpawnLogicSummary(MissionAgentSpawnLogic spawnLogic)
        {
            if (spawnLogic == null)
                return "SpawnLogic=null";

            var builder = new StringBuilder();
            builder.Append("SpawnLogicType=");
            builder.Append(spawnLogic.GetType().Name);

            object playerSide = MissionAgentSpawnLogicPlayerSideField?.GetValue(spawnLogic);
            builder.Append(" NativePlayerSide=");
            builder.Append(playerSide == null ? "null" : playerSide.GetType().GetField("_side", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(playerSide)?.ToString() ?? playerSide.ToString());

            object deploymentPlan = MissionAgentSpawnLogicDeploymentPlanField?.GetValue(spawnLogic);
            builder.Append(" NativeDeploymentPlan=");
            builder.Append(deploymentPlan == null ? "null" : deploymentPlan.GetType().Name);

            int[] troopTotals = MissionAgentSpawnLogicNumberOfTroopsInTotalField?.GetValue(spawnLogic) as int[];
            builder.Append(" TroopTotals=");
            builder.Append(
                troopTotals == null
                    ? "null"
                    : "[" + string.Join(", ", troopTotals.Select((value, index) => ((BattleSideEnum)index) + "=" + value)) + "]");

            Array phases = MissionAgentSpawnLogicPhasesField?.GetValue(spawnLogic) as Array;
            builder.Append(" PhaseCounts=");
            if (phases == null)
            {
                builder.Append("null");
            }
            else
            {
                var phaseEntries = new List<string>();
                for (int i = 0; i < phases.Length; i++)
                {
                    object phaseList = phases.GetValue(i);
                    int count = (int)(phaseList?.GetType().GetProperty("Count")?.GetValue(phaseList) ?? -1);
                    phaseEntries.Add(((BattleSideEnum)i) + "=" + count);
                }
                builder.Append("[" + string.Join(", ", phaseEntries) + "]");
            }

            Array missionSides = MissionAgentSpawnLogicMissionSidesField?.GetValue(spawnLogic) as Array;
            builder.Append(" MissionSides=");
            if (missionSides == null)
            {
                builder.Append("null");
            }
            else
            {
                var sideEntries = new List<string>();
                for (int i = 0; i < missionSides.Length; i++)
                {
                    object sideState = missionSides.GetValue(i);
                    sideEntries.Add(((BattleSideEnum)i) + "=" + (sideState == null ? "null" : sideState.GetType().Name));
                }
                builder.Append("[" + string.Join(", ", sideEntries) + "]");
            }

            return builder.ToString();
        }

        public static void TrySyncReinforcementState(Mission mission, bool enabled, string source)
        {
            if (!IsActive(mission) || _reinforcementsEnabled == enabled)
                return;

            _activeSpawnLogic.SetReinforcementsSpawnEnabled(enabled);
            _reinforcementsEnabled = enabled;
            ModLogger.Info(
                "ExactCampaignArmyBootstrap: reinforcement gate updated. " +
                "Scene=" + (mission?.SceneName ?? "null") +
                " Enabled=" + enabled +
                " PlayerSide=" + _activePlayerSide +
                " Source=" + (source ?? "unknown"));
        }

        public static bool TryGetEntryId(Agent agent, out string entryId)
        {
            if (agent?.Origin is ExactCampaignSnapshotAgentOrigin origin &&
                !string.IsNullOrWhiteSpace(origin.EntryId))
            {
                entryId = origin.EntryId;
                return true;
            }

            entryId = null;
            return false;
        }

        public static bool TryGetSide(Agent agent, out BattleSideEnum side)
        {
            if (agent?.Origin is ExactCampaignSnapshotAgentOrigin origin)
            {
                side = origin.Side;
                return side != BattleSideEnum.None;
            }

            side = BattleSideEnum.None;
            return false;
        }

        public static void LogInitializationDeferred(Mission mission, string reason, string source)
        {
            DateTime nowUtc = DateTime.UtcNow;
            if (nowUtc < _nextDeferredLogUtc)
                return;

            _nextDeferredLogUtc = nowUtc.AddSeconds(2);
            ModLogger.Info(
                "ExactCampaignArmyBootstrap: deferred native-like army bootstrap initialization. " +
                "Scene=" + (mission?.SceneName ?? "null") +
                " Reason=" + (reason ?? "unknown") +
                " Source=" + (source ?? "unknown"));
        }

        private static void ComputeInitialSpawnCounts(
            int defenderTotal,
            int attackerTotal,
            out int defenderInitial,
            out int attackerInitial,
            out int battleSizeBudget)
        {
            defenderTotal = Math.Max(0, defenderTotal);
            attackerTotal = Math.Max(0, attackerTotal);
            int total = defenderTotal + attackerTotal;

            battleSizeBudget = BattleSnapshotRuntimeState.GetState()?.BattleSizeBudget ?? 0;
            if (battleSizeBudget <= 0)
                battleSizeBudget = total;

            if (total <= 0 || battleSizeBudget >= total)
            {
                defenderInitial = defenderTotal;
                attackerInitial = attackerTotal;
                return;
            }

            battleSizeBudget = Math.Max(1, battleSizeBudget);

            defenderInitial = defenderTotal > 0
                ? Math.Max(1, (int)Math.Round((double)battleSizeBudget * defenderTotal / total, MidpointRounding.AwayFromZero))
                : 0;
            defenderInitial = Math.Min(defenderTotal, defenderInitial);

            attackerInitial = Math.Min(attackerTotal, Math.Max(0, battleSizeBudget - defenderInitial));
            if (attackerTotal > 0 && attackerInitial <= 0)
            {
                attackerInitial = 1;
                if (defenderInitial > 1)
                    defenderInitial--;
            }

            if (defenderTotal > 0 && defenderInitial <= 0)
            {
                defenderInitial = 1;
                if (attackerInitial > 1)
                    attackerInitial--;
            }

            int overflow = defenderInitial + attackerInitial - battleSizeBudget;
            if (overflow > 0)
            {
                if (defenderInitial >= attackerInitial && defenderInitial > 1)
                    defenderInitial = Math.Max(1, defenderInitial - overflow);
                else if (attackerInitial > 1)
                    attackerInitial = Math.Max(1, attackerInitial - overflow);
            }
        }

        private static bool TryBuildSuppliers(
            BattleSideEnum playerSide,
            out IMissionTroopSupplier[] suppliers,
            out int defenderTotal,
            out int attackerTotal,
            out string diagnostics)
        {
            suppliers = null;
            defenderTotal = 0;
            attackerTotal = 0;
            diagnostics = string.Empty;

            BattleRuntimeState runtimeState = BattleSnapshotRuntimeState.GetState();
            if (runtimeState?.Sides == null || runtimeState.Sides.Count <= 0)
            {
                diagnostics = "runtime-state-missing";
                return false;
            }

            ExactCampaignSnapshotTroopSupplier defenderSupplier = BuildSupplier(runtimeState, BattleSideEnum.Defender, playerSide, out defenderTotal, out string defenderDiagnostics);
            ExactCampaignSnapshotTroopSupplier attackerSupplier = BuildSupplier(runtimeState, BattleSideEnum.Attacker, playerSide, out attackerTotal, out string attackerDiagnostics);
            suppliers = new IMissionTroopSupplier[2]
            {
                defenderSupplier,
                attackerSupplier
            };

            diagnostics =
                "Defender=" + defenderTotal + "(" + defenderDiagnostics + ")" +
                " Attacker=" + attackerTotal + "(" + attackerDiagnostics + ")";
            return defenderTotal > 0 || attackerTotal > 0;
        }

        private static void PushSpawnLogicInitTeamSideOverride(Mission mission, BattleSideEnum playerSide)
        {
            if (mission == null || playerSide == BattleSideEnum.None)
                return;

            if (_spawnLogicInitSideOverrideDepth == 0 || !ReferenceEquals(_spawnLogicInitSideOverrideMission, mission))
            {
                _spawnLogicInitSideOverrideMission = mission;
                _spawnLogicInitSideOverride = playerSide;
                _spawnLogicInitSideOverrideDepth = 1;
                return;
            }

            _spawnLogicInitSideOverrideDepth++;
        }

        private static void PopSpawnLogicInitTeamSideOverride(Mission mission)
        {
            if (_spawnLogicInitSideOverrideDepth <= 0 || !ReferenceEquals(_spawnLogicInitSideOverrideMission, mission))
                return;

            _spawnLogicInitSideOverrideDepth--;
            if (_spawnLogicInitSideOverrideDepth > 0)
                return;

            _spawnLogicInitSideOverrideMission = null;
            _spawnLogicInitSideOverride = BattleSideEnum.None;
        }

        private static List<TeamSideOverrideState> PushInitTeamSideSanitization(
            Mission mission,
            BattleSideEnum playerSide,
            string source)
        {
            var overrides = new List<TeamSideOverrideState>();
            if (mission?.Teams == null || playerSide == BattleSideEnum.None)
                return overrides;

            if (TeamSideBackingField == null)
            {
                ModLogger.Info(
                    "ExactCampaignArmyBootstrap: could not sanitize Team.Side=None during native bootstrap init because Team backing field was not found. " +
                    "Scene=" + (mission.SceneName ?? "null") +
                    " PlayerSide=" + playerSide +
                    " Source=" + (source ?? "unknown"));
                return overrides;
            }

            foreach (Team team in mission.Teams)
            {
                if (team == null || team.Side != BattleSideEnum.None)
                    continue;

                try
                {
                    TeamSideBackingField.SetValue(team, playerSide);
                    bool addedTemporaryDeploymentPlan = TryAddTemporaryDeploymentPlanForRemappedTeam(
                        mission,
                        team,
                        source,
                        out int temporaryDeploymentPlanIndex);
                    overrides.Add(
                        new TeamSideOverrideState(
                            team,
                            BattleSideEnum.None,
                            addedTemporaryDeploymentPlan,
                            temporaryDeploymentPlanIndex));
                }
                catch (Exception ex)
                {
                    ModLogger.Info(
                        "ExactCampaignArmyBootstrap: failed to temporarily remap Team.Side=None during native bootstrap init. " +
                        "Scene=" + (mission.SceneName ?? "null") +
                        " TeamIndex=" + team.TeamIndex +
                        " RequestedSide=" + playerSide +
                        " Error=" + ex.GetType().Name + ": " + ex.Message +
                        " Source=" + (source ?? "unknown"));
                }
            }

            if (overrides.Count > 0)
            {
                string overrideSummary = string.Join(
                    ", ",
                    overrides.Select(state => "#" + state.Team.TeamIndex + ":" + state.OriginalSide + "->" + playerSide));
                ModLogger.Info(
                    "ExactCampaignArmyBootstrap: temporarily remapped non-battle teams during native bootstrap init. " +
                    "Scene=" + (mission.SceneName ?? "null") +
                    " PlayerSide=" + playerSide +
                    " Teams=[" + overrideSummary + "] " +
                    " Source=" + (source ?? "unknown"));
            }

            return overrides;
        }

        private static void PopInitTeamSideSanitization(
            List<TeamSideOverrideState> overrides,
            string source)
        {
            if (overrides == null || overrides.Count == 0 || TeamSideBackingField == null)
                return;

            foreach (TeamSideOverrideState state in overrides)
            {
                if (state.Team == null)
                    continue;

                if (state.AddedTemporaryDeploymentPlan)
                {
                    TryRemoveTemporaryDeploymentPlanForRemappedTeam(state.Team.Mission, state.TemporaryDeploymentPlanIndex, source);
                }

                try
                {
                    TeamSideBackingField.SetValue(state.Team, state.OriginalSide);
                }
                catch (Exception ex)
                {
                    ModLogger.Info(
                        "ExactCampaignArmyBootstrap: failed to restore temporary Team.Side remap after native bootstrap init. " +
                        "Scene=" + (state.Team.Mission?.SceneName ?? "null") +
                        " TeamIndex=" + state.Team.TeamIndex +
                        " RestoreSide=" + state.OriginalSide +
                        " Error=" + ex.GetType().Name + ": " + ex.Message +
                        " Source=" + (source ?? "unknown"));
                }
            }
        }

        private static bool TryAddTemporaryDeploymentPlanForRemappedTeam(
            Mission mission,
            Team team,
            string source,
            out int addedIndex)
        {
            addedIndex = -1;
            if (mission == null || team == null)
                return false;

            if (!mission.GetDeploymentPlan<DefaultMissionDeploymentPlan>(out DefaultMissionDeploymentPlan deploymentPlan) ||
                deploymentPlan == null)
            {
                return false;
            }

            if (DefaultMissionDeploymentPlanTeamDeploymentPlansField == null)
            {
                ModLogger.Info(
                    "ExactCampaignArmyBootstrap: could not bridge deployment plan for remapped non-battle team because DefaultMissionDeploymentPlan field metadata was not found. " +
                    "Scene=" + (mission.SceneName ?? "null") +
                    " TeamIndex=" + team.TeamIndex +
                    " Source=" + (source ?? "unknown"));
                return false;
            }

            object teamPlans = DefaultMissionDeploymentPlanTeamDeploymentPlansField.GetValue(deploymentPlan);
            if (teamPlans == null)
                return false;

            PropertyInfo countProperty = teamPlans.GetType().GetProperty("Count", BindingFlags.Instance | BindingFlags.Public);
            MethodInfo addMethod = teamPlans.GetType().GetMethod("Add", BindingFlags.Instance | BindingFlags.Public);
            if (countProperty == null || addMethod == null)
                return false;

            addedIndex = (int)countProperty.GetValue(teamPlans);
            var teamPlanTuple = (team, new DefaultTeamDeploymentPlan(mission, team));
            addMethod.Invoke(teamPlans, new object[] { teamPlanTuple });
            ModLogger.Info(
                "ExactCampaignArmyBootstrap: added temporary deployment plan for remapped non-battle team during native bootstrap init. " +
                "Scene=" + (mission.SceneName ?? "null") +
                " TeamIndex=" + team.TeamIndex +
                " TeamSide=" + team.Side +
                " AddedIndex=" + addedIndex +
                " Source=" + (source ?? "unknown"));
            return true;
        }

        private static void TryRemoveTemporaryDeploymentPlanForRemappedTeam(
            Mission mission,
            int addedIndex,
            string source)
        {
            if (mission == null || addedIndex < 0)
                return;

            if (!mission.GetDeploymentPlan<DefaultMissionDeploymentPlan>(out DefaultMissionDeploymentPlan deploymentPlan) ||
                deploymentPlan == null ||
                DefaultMissionDeploymentPlanTeamDeploymentPlansField == null)
            {
                return;
            }

            object teamPlans = DefaultMissionDeploymentPlanTeamDeploymentPlansField.GetValue(deploymentPlan);
            if (teamPlans == null)
                return;

            PropertyInfo countProperty = teamPlans.GetType().GetProperty("Count", BindingFlags.Instance | BindingFlags.Public);
            MethodInfo removeAtMethod = teamPlans.GetType().GetMethod("RemoveAt", BindingFlags.Instance | BindingFlags.Public);
            if (countProperty == null || removeAtMethod == null)
                return;

            int count = (int)countProperty.GetValue(teamPlans);
            if (addedIndex < 0 || addedIndex >= count)
                return;

            removeAtMethod.Invoke(teamPlans, new object[] { addedIndex });
            ModLogger.Info(
                "ExactCampaignArmyBootstrap: removed temporary deployment plan for remapped non-battle team after native bootstrap init. " +
                "Scene=" + (mission.SceneName ?? "null") +
                " RemovedIndex=" + addedIndex +
                " Source=" + (source ?? "unknown"));
        }

        private static ExactCampaignSnapshotTroopSupplier BuildSupplier(
            BattleRuntimeState runtimeState,
            BattleSideEnum side,
            BattleSideEnum playerSide,
            out int totalHealthyCount,
            out string diagnostics)
        {
            totalHealthyCount = 0;
            diagnostics = "side-state-missing";

            BattleSideState sideState = runtimeState?.Sides?.FirstOrDefault(candidate => ResolveBattleSide(candidate) == side);
            if (sideState?.Entries == null || sideState.Entries.Count <= 0)
                return new ExactCampaignSnapshotTroopSupplier(side, side == playerSide);

            var supplier = new ExactCampaignSnapshotTroopSupplier(side, side == playerSide);
            var origins = new List<ExactCampaignSnapshotAgentOrigin>();
            BasicCharacterObject generalCharacter = null;
            int unresolvedEntries = 0;
            int skippedWoundedOnlyEntries = 0;
            int seed = 1;

            foreach (RosterEntryState entryState in sideState.Entries)
            {
                if (entryState == null)
                    continue;

                int availableCount = Math.Max(0, entryState.Count - entryState.WoundedCount);
                if (availableCount <= 0)
                {
                    skippedWoundedOnlyEntries++;
                    continue;
                }

                BasicCharacterObject troop = TryResolveEntryCharacter(entryState);
                if (troop == null)
                {
                    unresolvedEntries++;
                    continue;
                }

                if (generalCharacter == null &&
                    (entryState.IsHero || !string.IsNullOrWhiteSpace(entryState.HeroRole)))
                {
                    generalCharacter = troop;
                }

                string troopId = troop.StringId;
                for (int i = 0; i < availableCount; i++)
                {
                    origins.Add(new ExactCampaignSnapshotAgentOrigin(
                        supplier,
                        troop,
                        entryState.EntryId,
                        troopId,
                        side,
                        side == playerSide,
                        seed++));
                    totalHealthyCount++;
                }
            }

            if (generalCharacter == null)
                generalCharacter = origins.FirstOrDefault()?.Troop;

            supplier.Initialize(origins, generalCharacter);
            diagnostics =
                "Entries=" + sideState.Entries.Count +
                " Healthy=" + totalHealthyCount +
                " UnresolvedEntries=" + unresolvedEntries +
                " WoundedOnlyEntries=" + skippedWoundedOnlyEntries;
            return supplier;
        }

        private static BasicCharacterObject TryResolveEntryCharacter(RosterEntryState entryState)
        {
            if (entryState == null)
                return null;

            if (!string.IsNullOrWhiteSpace(entryState.EntryId))
            {
                BasicCharacterObject runtimeCharacter = BattleSnapshotRuntimeState.TryResolveCharacterObject(entryState.EntryId);
                if (runtimeCharacter != null)
                    return runtimeCharacter;
            }

            string[] candidateIds =
            {
                entryState.SpawnTemplateId,
                entryState.OriginalCharacterId,
                entryState.CharacterId,
                entryState.HeroTemplateId
            };

            foreach (string candidateId in candidateIds)
            {
                if (string.IsNullOrWhiteSpace(candidateId))
                    continue;

                try
                {
                    BasicCharacterObject candidate = MBObjectManager.Instance.GetObject<BasicCharacterObject>(candidateId);
                    if (candidate != null)
                        return candidate;
                }
                catch
                {
                }
            }

            return null;
        }

        private static BattleSideEnum ResolveBattleSide(BattleSideState sideState)
        {
            if (sideState == null)
                return BattleSideEnum.None;

            string raw =
                !string.IsNullOrWhiteSpace(sideState.CanonicalSideKey)
                    ? sideState.CanonicalSideKey
                    : sideState.SideId;
            if (string.Equals(raw, "attacker", StringComparison.OrdinalIgnoreCase))
                return BattleSideEnum.Attacker;
            if (string.Equals(raw, "defender", StringComparison.OrdinalIgnoreCase))
                return BattleSideEnum.Defender;
            return BattleSideEnum.None;
        }
    }

    public sealed class ExactCampaignSnapshotTroopSupplier : IMissionTroopSupplier
    {
        private readonly bool _isPlayerSide;
        private List<ExactCampaignSnapshotAgentOrigin> _troops = new List<ExactCampaignSnapshotAgentOrigin>();
        private BasicCharacterObject _generalCharacter;
        private int _allocatedCount;
        private int _numWounded;
        private int _numKilled;
        private int _numRouted;

        public BattleSideEnum Side { get; }

        public int NumRemovedTroops => _numWounded + _numKilled + _numRouted;

        public int NumTroopsNotSupplied => Math.Max(0, _troops.Count - _allocatedCount);

        public bool AnyTroopRemainsToBeSupplied => _allocatedCount < _troops.Count;

        public ExactCampaignSnapshotTroopSupplier(BattleSideEnum side, bool isPlayerSide)
        {
            Side = side;
            _isPlayerSide = isPlayerSide;
        }

        public void Initialize(List<ExactCampaignSnapshotAgentOrigin> troops, BasicCharacterObject generalCharacter)
        {
            _troops = troops ?? new List<ExactCampaignSnapshotAgentOrigin>();
            _generalCharacter = generalCharacter;
            _allocatedCount = 0;
            _numWounded = 0;
            _numKilled = 0;
            _numRouted = 0;
        }

        public IEnumerable<IAgentOriginBase> SupplyTroops(int numberToAllocate)
        {
            if (numberToAllocate <= 0 || _allocatedCount >= _troops.Count)
                return Array.Empty<IAgentOriginBase>();

            int takeCount = Math.Min(numberToAllocate, _troops.Count - _allocatedCount);
            var supplied = new List<IAgentOriginBase>(takeCount);
            for (int i = 0; i < takeCount; i++)
            {
                supplied.Add(_troops[_allocatedCount]);
                _allocatedCount++;
            }

            return supplied;
        }

        public IAgentOriginBase SupplyOneTroop()
        {
            if (_allocatedCount >= _troops.Count)
                return null;

            ExactCampaignSnapshotAgentOrigin troop = _troops[_allocatedCount];
            _allocatedCount++;
            return troop;
        }

        public IEnumerable<IAgentOriginBase> GetAllTroops()
        {
            return _troops;
        }

        public BasicCharacterObject GetGeneralCharacter()
        {
            return _generalCharacter;
        }

        public int GetNumberOfPlayerControllableTroops()
        {
            return _isPlayerSide ? _troops.Count : 0;
        }

        internal void OnOriginWounded(ExactCampaignSnapshotAgentOrigin origin)
        {
            _numWounded++;
        }

        internal void OnOriginKilled(ExactCampaignSnapshotAgentOrigin origin)
        {
            _numKilled++;
        }

        internal void OnOriginRouted(ExactCampaignSnapshotAgentOrigin origin)
        {
            _numRouted++;
        }
    }

    public sealed class ExactCampaignSnapshotAgentOrigin : IAgentOriginBase
    {
        private readonly ExactCampaignSnapshotTroopSupplier _supplier;
        private readonly BasicCharacterObject _troop;
        private readonly bool _isUnderPlayersCommand;
        private readonly int _seed;
        private readonly bool _hasThrownWeapon;
        private readonly bool _hasHeavyArmor;
        private readonly bool _hasShield;
        private readonly bool _hasSpear;
        private OriginRemovalState _removalState;

        private enum OriginRemovalState
        {
            Alive = 0,
            Wounded = 1,
            Killed = 2,
            Routed = 3
        }

        public string EntryId { get; }

        public string TroopId { get; }

        public BattleSideEnum Side { get; }

        public BasicCharacterObject Troop => _troop;

        bool IAgentOriginBase.IsUnderPlayersCommand => _isUnderPlayersCommand;

        uint IAgentOriginBase.FactionColor => 0u;

        uint IAgentOriginBase.FactionColor2 => 0u;

        IBattleCombatant IAgentOriginBase.BattleCombatant => null;

        int IAgentOriginBase.UniqueSeed => _seed;

        int IAgentOriginBase.Seed => _seed;

        Banner IAgentOriginBase.Banner => null;

        BasicCharacterObject IAgentOriginBase.Troop => _troop;

        bool IAgentOriginBase.HasThrownWeapon => _hasThrownWeapon;

        bool IAgentOriginBase.HasHeavyArmor => _hasHeavyArmor;

        bool IAgentOriginBase.HasShield => _hasShield;

        bool IAgentOriginBase.HasSpear => _hasSpear;

        public ExactCampaignSnapshotAgentOrigin(
            ExactCampaignSnapshotTroopSupplier supplier,
            BasicCharacterObject troop,
            string entryId,
            string troopId,
            BattleSideEnum side,
            bool isUnderPlayersCommand,
            int seed)
        {
            _supplier = supplier;
            _troop = troop;
            EntryId = entryId ?? string.Empty;
            TroopId = troopId ?? troop?.StringId ?? string.Empty;
            Side = side;
            _isUnderPlayersCommand = isUnderPlayersCommand;
            _seed = seed;
            AgentOriginUtilities.GetDefaultTroopTraits(_troop, out _hasThrownWeapon, out _hasSpear, out _hasShield, out _hasHeavyArmor);
        }

        void IAgentOriginBase.SetWounded()
        {
            if (!TryMarkRemoved(OriginRemovalState.Wounded))
                return;

            _supplier?.OnOriginWounded(this);
        }

        void IAgentOriginBase.SetKilled()
        {
            if (!TryMarkRemoved(OriginRemovalState.Killed))
                return;

            _supplier?.OnOriginKilled(this);
        }

        void IAgentOriginBase.SetRouted(bool isOrderRetreat)
        {
            if (!TryMarkRemoved(OriginRemovalState.Routed))
                return;

            _supplier?.OnOriginRouted(this);
        }

        void IAgentOriginBase.OnAgentRemoved(float agentHealth)
        {
        }

        void IAgentOriginBase.OnScoreHit(BasicCharacterObject victim, BasicCharacterObject formationCaptain, int damage, bool isFatal, bool isTeamKill, WeaponComponentData attackerWeapon)
        {
        }

        void IAgentOriginBase.SetBanner(Banner banner)
        {
        }

        TroopTraitsMask IAgentOriginBase.GetTraitsMask()
        {
            return AgentOriginUtilities.GetDefaultTraitsMask(this);
        }

        private bool TryMarkRemoved(OriginRemovalState targetState)
        {
            if (_removalState != OriginRemovalState.Alive)
                return false;

            _removalState = targetState;
            return true;
        }
    }
}
