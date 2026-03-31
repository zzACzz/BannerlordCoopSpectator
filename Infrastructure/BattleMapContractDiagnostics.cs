using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.Infrastructure
{
    /// <summary>
    /// Central diagnostics for the battle-map runtime contract:
    /// MissionState.OpenNew overloads, mission initializer patch state, live mission
    /// map-patch/spawn-path facts, deployment plan summaries, and scene spawn entry coverage.
    /// The goal is to make the exact failure layer visible in logs without changing runtime behavior.
    /// </summary>
    public static class BattleMapContractDiagnostics
    {
        private static readonly PropertyInfo MissionInitializerRecordProperty =
            typeof(Mission).GetProperty("InitializerRecord", BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly FieldInfo DefaultMissionDeploymentPlanFormationSceneSpawnEntriesField =
            typeof(DefaultMissionDeploymentPlan).GetField("_formationSceneSpawnEntries", BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly HashSet<string> LoggedKeys = new HashSet<string>(StringComparer.Ordinal);

        public static void LogMissionStateOpenNewContract(string source)
        {
            if (!ExperimentalFeatures.EnableBattleMapFullContractDiagnostics)
                return;

            string safeSource = string.IsNullOrWhiteSpace(source) ? "unknown" : source;
            string logKey = "opennew-contract|" + safeSource;
            if (!LoggedKeys.Add(logKey))
                return;

            try
            {
                List<string> overloads = typeof(MissionState)
                    .GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .Where(method => string.Equals(method.Name, "OpenNew", StringComparison.Ordinal))
                    .Select(BuildMethodSignature)
                    .OrderBy(signature => signature, StringComparer.Ordinal)
                    .ToList();

                ModLogger.Info(
                    "BattleMapContractDiagnostics: MissionState.OpenNew contract snapshot. " +
                    "Source=" + safeSource +
                    " OverloadCount=" + overloads.Count +
                    " Overloads=[" + string.Join(" | ", overloads) + "]");
            }
            catch (Exception ex)
            {
                ModLogger.Info(
                    "BattleMapContractDiagnostics: MissionState.OpenNew contract snapshot failed. " +
                    "Source=" + safeSource +
                    " Message=" + ex.Message);
            }
        }

        public static void LogMissionInitializerRecordState(MissionInitializerRecord record, string source)
        {
            if (!ExperimentalFeatures.EnableBattleMapFullContractDiagnostics)
                return;

            string safeSource = string.IsNullOrWhiteSpace(source) ? "unknown" : source;
            ModLogger.Info(
                "BattleMapContractDiagnostics: mission initializer record. " +
                "Source=" + safeSource +
                " " + BuildInitializerRecordSummary(record));
        }

        public static void LogMissionRuntimeContract(Mission mission, string source)
        {
            if (!ExperimentalFeatures.EnableBattleMapFullContractDiagnostics || mission == null)
                return;

            string safeSource = string.IsNullOrWhiteSpace(source) ? "unknown" : source;
            string logKey = "mission-runtime|" + RuntimeHelpers.GetHashCode(mission) + "|" + safeSource;
            if (!LoggedKeys.Add(logKey))
                return;

            try
            {
                bool hasSceneMapPatch = SafeBool(() => mission.HasSceneMapPatch());
                bool hasSpawnPath = SafeBool(() => mission.HasSpawnPath);
                string mode = SafeString(() => mission.Mode.ToString());
                bool isFieldBattle = SafeBool(() => mission.IsFieldBattle);
                bool isSiegeBattle = SafeBool(() => mission.IsSiegeBattle);
                string boundarySummary = BuildBoundarySummary(mission);
                string reflectedRecordSummary = BuildReflectedInitializerRecordSummary(mission);

                ModLogger.Info(
                    "BattleMapContractDiagnostics: mission runtime contract. " +
                    "Source=" + safeSource +
                    " Scene=" + (mission.SceneName ?? "null") +
                    " Mode=" + (mode ?? "unknown") +
                    " IsFieldBattle=" + isFieldBattle +
                    " IsSiegeBattle=" + isSiegeBattle +
                    " HasSpawnPath=" + hasSpawnPath +
                    " HasSceneMapPatch=" + hasSceneMapPatch +
                    " Boundaries=" + boundarySummary +
                    " ReflectedInitializerRecord={" + reflectedRecordSummary + "}");

                ModLogger.Info(
                    "BattleMapContractDiagnostics: mission patch/spawn-path snapshot. " +
                    "Source=" + safeSource +
                    " PatchPosition=" + BuildPatchPositionSummary(mission) +
                    " PatchDirection=" + BuildPatchDirectionSummary(mission) +
                    " AttackerInitialPath={" + BuildSpawnPathSummary(mission, BattleSideEnum.Attacker) + "}" +
                    " DefenderInitialPath={" + BuildSpawnPathSummary(mission, BattleSideEnum.Defender) + "}" +
                    " AttackerReinforcementPathCount=" + GetReinforcementPathCount(mission, BattleSideEnum.Attacker) +
                    " DefenderReinforcementPathCount=" + GetReinforcementPathCount(mission, BattleSideEnum.Defender) +
                    ".");
            }
            catch (Exception ex)
            {
                ModLogger.Info(
                    "BattleMapContractDiagnostics: mission runtime contract snapshot failed. " +
                    "Source=" + safeSource +
                    " Scene=" + (mission.SceneName ?? "null") +
                    " Message=" + ex.Message);
            }
        }

        public static void LogDeploymentPlanContract(
            Mission mission,
            Team focusTeam,
            FormationClass? focusFormation,
            bool isReinforcement,
            string source)
        {
            if (!ExperimentalFeatures.EnableBattleMapFullContractDiagnostics || mission == null)
                return;

            string safeSource = string.IsNullOrWhiteSpace(source) ? "unknown" : source;
            int missionKey = RuntimeHelpers.GetHashCode(mission);
            int teamIndex = focusTeam?.TeamIndex ?? -1;
            string formationKey = focusFormation?.ToString() ?? "all";
            string logKey = "deployment-plan|" + missionKey + "|" + teamIndex + "|" + formationKey + "|" + isReinforcement + "|" + safeSource;
            if (!LoggedKeys.Add(logKey))
                return;

            if (!mission.GetDeploymentPlan<DefaultMissionDeploymentPlan>(out DefaultMissionDeploymentPlan deploymentPlan) || deploymentPlan == null)
            {
                ModLogger.Info(
                    "BattleMapContractDiagnostics: deployment plan contract unavailable. " +
                    "Source=" + safeSource +
                    " Scene=" + (mission.SceneName ?? "null") +
                    " FocusTeamIndex=" + teamIndex +
                    " FocusFormation=" + formationKey +
                    " IsReinforcement=" + isReinforcement);
                return;
            }

            try
            {
                List<string> teamSummaries = new List<string>();
                if (mission.Teams != null)
                {
                    foreach (Team team in mission.Teams)
                    {
                        if (team == null || team.Side == BattleSideEnum.None)
                            continue;

                        teamSummaries.Add(BuildTeamDeploymentSummary(deploymentPlan, team, focusFormation));
                    }
                }

                ModLogger.Info(
                    "BattleMapContractDiagnostics: deployment plan contract. " +
                    "Source=" + safeSource +
                    " Scene=" + (mission.SceneName ?? "null") +
                    " FocusTeamIndex=" + teamIndex +
                    " FocusFormation=" + formationKey +
                    " IsReinforcement=" + isReinforcement +
                    " SceneSpawnEntries={" + BuildSceneSpawnEntrySummary(deploymentPlan, focusFormation) + "}" +
                    " TeamPlans=[" + string.Join(" | ", teamSummaries) + "]");
            }
            catch (Exception ex)
            {
                ModLogger.Info(
                    "BattleMapContractDiagnostics: deployment plan contract snapshot failed. " +
                    "Source=" + safeSource +
                    " Scene=" + (mission.SceneName ?? "null") +
                    " FocusTeamIndex=" + teamIndex +
                    " FocusFormation=" + formationKey +
                    " Message=" + ex.Message);
            }
        }

        private static string BuildMethodSignature(MethodInfo method)
        {
            if (method == null)
                return "null";

            ParameterInfo[] parameters = method.GetParameters();
            string parameterSummary = string.Join(
                ", ",
                parameters.Select(parameter => (parameter.ParameterType?.Name ?? "null") + " " + parameter.Name));
            return (method.ReturnType?.Name ?? "void") + " " + method.Name + "(" + parameterSummary + ")";
        }

        private static string BuildInitializerRecordSummary(MissionInitializerRecord record)
        {
            return
                "SceneName=" + (record.SceneName ?? "null") +
                " SceneLevels=" + (record.SceneLevels ?? "null") +
                " PlayingInCampaignMode=" + record.PlayingInCampaignMode +
                " SceneHasMapPatch=" + record.SceneHasMapPatch +
                " PatchCoordinates=" + FormatVec2(record.PatchCoordinates) +
                " PatchEncounterDir=" + FormatVec2(record.PatchEncounterDir);
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
                    "SceneName=" + FormatValue(TryReadMember(record, "SceneName")) +
                    " SceneLevels=" + FormatValue(TryReadMember(record, "SceneLevels")) +
                    " PlayingInCampaignMode=" + FormatValue(TryReadMember(record, "PlayingInCampaignMode")) +
                    " SceneHasMapPatch=" + FormatValue(TryReadMember(record, "SceneHasMapPatch")) +
                    " PatchCoordinates=" + FormatValue(TryReadMember(record, "PatchCoordinates")) +
                    " PatchEncounterDir=" + FormatValue(TryReadMember(record, "PatchEncounterDir"));
            }
            catch (Exception ex)
            {
                return "reflection-failed:" + ex.Message;
            }
        }

        private static string BuildBoundarySummary(Mission mission)
        {
            if (mission?.Boundaries == null)
                return "Count=0";

            var boundaryIds = new List<string>();
            try
            {
                foreach (KeyValuePair<string, ICollection<Vec2>> boundary in mission.Boundaries)
                {
                    boundaryIds.Add(boundary.Key ?? "null");
                }
            }
            catch (Exception ex)
            {
                return "enumeration-failed:" + ex.Message;
            }

            IEnumerable<string> sample = boundaryIds.Take(8);
            return
                "Count=" + boundaryIds.Count +
                " Sample=[" + string.Join(", ", sample) + "]" +
                (boundaryIds.Count > 8 ? " Truncated=True" : string.Empty);
        }

        private static string BuildPatchPositionSummary(Mission mission)
        {
            try
            {
                return mission.GetPatchSceneEncounterPosition(out Vec3 position)
                    ? FormatVec3(position)
                    : "invalid";
            }
            catch (Exception ex)
            {
                return "failed:" + ex.Message;
            }
        }

        private static string BuildPatchDirectionSummary(Mission mission)
        {
            try
            {
                return mission.GetPatchSceneEncounterDirection(out Vec2 direction)
                    ? FormatVec2(direction)
                    : "invalid";
            }
            catch (Exception ex)
            {
                return "failed:" + ex.Message;
            }
        }

        private static string BuildSpawnPathSummary(Mission mission, BattleSideEnum side)
        {
            try
            {
                SpawnPathData spawnPathData = mission.GetInitialSpawnPathData(side);
                if (!spawnPathData.IsValid)
                    return "Valid=False";

                return
                    "Valid=True" +
                    " PivotRatio=" + spawnPathData.PivotRatio.ToString("0.###", CultureInfo.InvariantCulture) +
                    " IsInverted=" + spawnPathData.IsInverted +
                    " SnapType=" + spawnPathData.SnapType +
                    " PathPoints=" + (spawnPathData.Path?.NumberOfPoints ?? 0);
            }
            catch (Exception ex)
            {
                return "failed:" + ex.Message;
            }
        }

        private static int GetReinforcementPathCount(Mission mission, BattleSideEnum side)
        {
            try
            {
                MBReadOnlyList<SpawnPathData> paths = mission.GetReinforcementPathsDataOfSide(side);
                return paths?.Count ?? 0;
            }
            catch
            {
                return -1;
            }
        }

        private static string BuildSceneSpawnEntrySummary(DefaultMissionDeploymentPlan deploymentPlan, FormationClass? focusFormation)
        {
            Array entries = DefaultMissionDeploymentPlanFormationSceneSpawnEntriesField?.GetValue(deploymentPlan) as Array;
            if (entries == null)
                return "entries=null";

            int sideCount = entries.GetLength(0);
            int formationCount = entries.GetLength(1);
            var summaries = new List<string>();

            for (int sideIndex = 0; sideIndex < sideCount; sideIndex++)
            {
                int spawnEntityCount = 0;
                int reinforcementEntityCount = 0;
                var missingInitial = new List<string>();
                var missingReinforcement = new List<string>();

                for (int formationIndex = 0; formationIndex < formationCount; formationIndex++)
                {
                    FormationSceneSpawnEntry entry = (FormationSceneSpawnEntry)entries.GetValue(sideIndex, formationIndex);
                    if (entry.SpawnEntity != null)
                        spawnEntityCount++;
                    else if (formationIndex < (int)FormationClass.NumberOfRegularFormations)
                        missingInitial.Add(((FormationClass)formationIndex).ToString());

                    if (entry.ReinforcementSpawnEntity != null)
                        reinforcementEntityCount++;
                    else if (formationIndex < (int)FormationClass.NumberOfRegularFormations)
                        missingReinforcement.Add(((FormationClass)formationIndex).ToString());
                }

                summaries.Add(
                    ResolveSideLabel(sideIndex) +
                    "{InitialEntities=" + spawnEntityCount + "/" + formationCount +
                    " ReinforcementEntities=" + reinforcementEntityCount + "/" + formationCount +
                    " MissingInitial=[" + string.Join(", ", missingInitial.Take(6)) + "]" +
                    (missingInitial.Count > 6 ? "..." : string.Empty) +
                    " MissingReinforcement=[" + string.Join(", ", missingReinforcement.Take(6)) + "]" +
                    (missingReinforcement.Count > 6 ? "..." : string.Empty) +
                    "}");
            }

            if (focusFormation.HasValue)
            {
                int focusIndex = (int)focusFormation.Value;
                if (focusIndex >= 0 && focusIndex < formationCount)
                {
                    for (int sideIndex = 0; sideIndex < sideCount; sideIndex++)
                    {
                        FormationSceneSpawnEntry entry = (FormationSceneSpawnEntry)entries.GetValue(sideIndex, focusIndex);
                        summaries.Add(
                            "Focus" + ResolveSideLabel(sideIndex) +
                            "{Formation=" + focusFormation.Value +
                            " InitialEntity=" + (entry.SpawnEntity != null) +
                            " ReinforcementEntity=" + (entry.ReinforcementSpawnEntity != null) +
                            " EntryFormationClass=" + entry.FormationClass +
                            "}");
                    }
                }
            }

            return string.Join(" | ", summaries);
        }

        private static string BuildTeamDeploymentSummary(
            DefaultMissionDeploymentPlan deploymentPlan,
            Team team,
            FormationClass? focusFormation)
        {
            string planSummary =
                "TeamSide=" + team.Side +
                " TeamIndex=" + team.TeamIndex +
                " InitialPlanMade=" + SafeBool(() => deploymentPlan.IsPlanMade(team)) +
                " ReinforcementPlanMade=" + SafeBool(() => deploymentPlan.IsReinforcementPlanMade(team)) +
                " InitialTroopCount=" + SafeInt(() => deploymentPlan.GetTroopCount(team)) +
                " ReinforcementTroopCount=" + SafeInt(() => deploymentPlan.GetTroopCount(team, isReinforcement: true)) +
                " SpawnPathOffset=" + SafeFloat(() => deploymentPlan.GetSpawnPathOffset(team)) +
                " TargetOffset=" + SafeFloat(() => deploymentPlan.GetTargetOffset(team)) +
                " HasDeploymentBoundaries=" + SafeBool(() => deploymentPlan.HasDeploymentBoundaries(team));

            List<string> formationSummaries = new List<string>();
            for (int formationIndex = 0; formationIndex < (int)FormationClass.NumberOfRegularFormations; formationIndex++)
            {
                FormationClass formationClass = (FormationClass)formationIndex;
                if (focusFormation.HasValue && formationClass != focusFormation.Value)
                    continue;

                string formationSummary = BuildFormationPlanSummary(deploymentPlan, team, formationClass);
                if (!string.IsNullOrEmpty(formationSummary))
                    formationSummaries.Add(formationSummary);
            }

            if (!focusFormation.HasValue && formationSummaries.Count == 0)
                formationSummaries.Add("NoRegularFormationPlans");

            return planSummary + " FormationPlans=[" + string.Join(", ", formationSummaries) + "]";
        }

        private static string BuildFormationPlanSummary(
            DefaultMissionDeploymentPlan deploymentPlan,
            Team team,
            FormationClass formationClass)
        {
            IFormationDeploymentPlan initialPlan = SafeGetFormationPlan(deploymentPlan, team, formationClass, isReinforcement: false);
            IFormationDeploymentPlan reinforcementPlan = SafeGetFormationPlan(deploymentPlan, team, formationClass, isReinforcement: true);
            if (!ShouldIncludeFormationPlan(initialPlan, reinforcementPlan))
                return string.Empty;

            return
                formationClass +
                "{Init=" + BuildSingleFormationPlanSummary(initialPlan) +
                ";Reinf=" + BuildSingleFormationPlanSummary(reinforcementPlan) +
                "}";
        }

        private static IFormationDeploymentPlan SafeGetFormationPlan(
            DefaultMissionDeploymentPlan deploymentPlan,
            Team team,
            FormationClass formationClass,
            bool isReinforcement)
        {
            try
            {
                return deploymentPlan.GetFormationPlan(team, formationClass, isReinforcement);
            }
            catch
            {
                return null;
            }
        }

        private static bool ShouldIncludeFormationPlan(IFormationDeploymentPlan initialPlan, IFormationDeploymentPlan reinforcementPlan)
        {
            return HasFormationPlanSignal(initialPlan) || HasFormationPlanSignal(reinforcementPlan);
        }

        private static bool HasFormationPlanSignal(IFormationDeploymentPlan plan)
        {
            if (plan == null)
                return false;

            try
            {
                return plan.PlannedTroopCount > 0 || plan.HasDimensions || plan.HasFrame();
            }
            catch
            {
                return true;
            }
        }

        private static string BuildSingleFormationPlanSummary(IFormationDeploymentPlan plan)
        {
            if (plan == null)
                return "null";

            bool hasFrame = SafeBool(() => plan.HasFrame());
            string summary =
                "Troops=" + SafeInt(() => plan.PlannedTroopCount) +
                " HasDimensions=" + SafeBool(() => plan.HasDimensions) +
                " Width=" + SafeFloat(() => plan.PlannedWidth) +
                " Depth=" + SafeFloat(() => plan.PlannedDepth) +
                " HasFrame=" + hasFrame +
                " SpawnClass=" + SafeString(() => plan.SpawnClass.ToString());

            if (hasFrame)
            {
                summary +=
                    " Position=" + SafeString(() => FormatVec3(plan.GetPosition())) +
                    " Direction=" + SafeString(() => FormatVec2(plan.GetDirection()));
            }

            return summary;
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

        private static string ResolveSideLabel(int sideIndex)
        {
            if (sideIndex == (int)BattleSideEnum.Attacker)
                return BattleSideEnum.Attacker.ToString();
            if (sideIndex == (int)BattleSideEnum.Defender)
                return BattleSideEnum.Defender.ToString();
            return "Side" + sideIndex;
        }

        private static bool SafeBool(Func<bool> getter)
        {
            try
            {
                return getter();
            }
            catch
            {
                return false;
            }
        }

        private static int SafeInt(Func<int> getter)
        {
            try
            {
                return getter();
            }
            catch
            {
                return -1;
            }
        }

        private static string SafeFloat(Func<float> getter)
        {
            try
            {
                return getter().ToString("0.###", CultureInfo.InvariantCulture);
            }
            catch
            {
                return "n/a";
            }
        }

        private static string SafeString(Func<string> getter)
        {
            try
            {
                return getter() ?? "null";
            }
            catch (Exception ex)
            {
                return "failed:" + ex.Message;
            }
        }

        private static string FormatValue(object value)
        {
            if (value == null)
                return "null";
            if (value is Vec2 vec2)
                return FormatVec2(vec2);
            if (value is Vec3 vec3)
                return FormatVec3(vec3);
            return Convert.ToString(value, CultureInfo.InvariantCulture) ?? "null";
        }

        private static string FormatVec2(Vec2 value)
        {
            return "(" +
                value.x.ToString("0.###", CultureInfo.InvariantCulture) +
                ", " +
                value.y.ToString("0.###", CultureInfo.InvariantCulture) +
                ")";
        }

        private static string FormatVec3(Vec3 value)
        {
            return "(" +
                value.x.ToString("0.###", CultureInfo.InvariantCulture) +
                ", " +
                value.y.ToString("0.###", CultureInfo.InvariantCulture) +
                ", " +
                value.z.ToString("0.###", CultureInfo.InvariantCulture) +
                ")";
        }
    }
}
