using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CoopSpectator.GameMode;
using CoopSpectator.Infrastructure;
using CoopSpectator.MissionBehaviors;
using HarmonyLib;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.Patches
{
    /// <summary>
    /// Intercepts only the listed TeamDeathmatch mission open and assembles
    /// an explicit minimal listed-ingress behavior stack for the coop runtime.
    /// </summary>
    public static class MissionStateOpenNewPatches
    {
        private const string OfficialTeamDeathmatchMissionName = "MultiplayerTeamDeathmatch";
        private const string OfficialBattleMissionName = "MultiplayerBattle";

        public static void Apply(Harmony harmony)
        {
            try
            {
                Type missionStateType = typeof(MissionState);
                MethodInfo openNew = missionStateType
                    .GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .FirstOrDefault(m =>
                    {
                        if (m.Name != "OpenNew")
                            return false;

                        ParameterInfo[] ps = m.GetParameters();
                        return ps.Length == 5
                            && ps[0].ParameterType == typeof(string)
                            && ps[1].ParameterType == typeof(MissionInitializerRecord)
                            && ps[2].ParameterType == typeof(InitializeMissionBehaviorsDelegate)
                            && ps[3].ParameterType == typeof(bool)
                            && ps[4].ParameterType == typeof(bool);
                    });

                if (openNew == null)
                {
                    ModLogger.Info("MissionStateOpenNewPatches: OpenNew(string, MissionInitializerRecord, InitializeMissionBehaviorsDelegate, bool, bool) not found. Skip.");
                    return;
                }

                MethodInfo prefix = typeof(MissionStateOpenNewPatches).GetMethod(nameof(OpenNew_Prefix), BindingFlags.Public | BindingFlags.Static);
                if (prefix == null)
                {
                    ModLogger.Info("MissionStateOpenNewPatches: prefix method not found. Skip.");
                    return;
                }

                harmony.Patch(openNew, prefix: new HarmonyMethod(prefix));
                ModLogger.Info("MissionStateOpenNewPatches: OpenNew prefix applied (listed TeamDeathmatch shell wrapping ready).");
            }
            catch (Exception ex)
            {
                ModLogger.Error("MissionStateOpenNewPatches.Apply failed.", ex);
            }
        }

        public static void OpenNew_Prefix(
            string missionName,
            ref MissionInitializerRecord rec,
            ref InitializeMissionBehaviorsDelegate handler,
            bool addDefaultMissionBehaviors,
            bool needsMemoryCleanup)
        {
            bool isOfficialBattleMission = string.Equals(missionName, OfficialBattleMissionName, StringComparison.Ordinal);
            bool isCoopBattleFactory = IsCoopBattleBehaviorFactory(handler);
            if (GameNetwork.IsServer && isOfficialBattleMission)
                PendingBattleMissionStartupState.Arm(rec.SceneName, "MissionState.OpenNew prefix");

            if (!ShouldWrapListedTeamDeathmatchShell(missionName))
                return;

            if (isCoopBattleFactory)
            {
                ModLogger.Info("MissionState.OpenNew: skip listed TeamDeathmatch shell wrapping for CoopBattle custom behavior factory.");
                return;
            }

            handler = CreateListedTeamDeathmatchIngressBehaviors;
            ModLogger.Info("MissionState.OpenNew: replaced TeamDeathmatch behavior handler with explicit listed-shell coop ingress assembly.");
        }

        private static bool ShouldWrapListedTeamDeathmatchShell(string missionName)
        {
            return string.Equals(missionName, OfficialTeamDeathmatchMissionName, StringComparison.Ordinal);
        }

        private static bool IsCoopBattleBehaviorFactory(InitializeMissionBehaviorsDelegate handler)
        {
            if (handler == null)
                return false;

            Type declaringType = handler.Method?.DeclaringType;
            Type targetType = handler.Target?.GetType();

            return IsCoopBattleType(declaringType) || IsCoopBattleType(targetType);
        }

        private static bool IsCoopBattleType(Type type)
        {
            if (type == null)
                return false;

            if (type == typeof(MissionMultiplayerCoopBattleMode))
                return true;

            string fullName = type.FullName ?? string.Empty;
            return fullName.IndexOf(nameof(MissionMultiplayerCoopBattleMode), StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static IEnumerable<MissionBehavior> CreateListedTeamDeathmatchIngressBehaviors(Mission mission)
        {
            List<MissionBehavior> list = GameNetwork.IsServer
                ? BuildListedTeamDeathmatchServerBehaviors(mission)
                : BuildListedTeamDeathmatchClientBehaviors(mission);

            if (GameNetwork.IsServer)
            {
                list.Add(new CoopMissionNetworkBridge());
                list.Add(new CoopMissionSpawnLogic());
                ModLogger.Info("MissionStateOpenNewPatches: appended CoopMissionNetworkBridge to explicit listed TeamDeathmatch server ingress.");
                ModLogger.Info("MissionStateOpenNewPatches: appended CoopMissionSpawnLogic to explicit listed TeamDeathmatch server ingress.");
            }
            else
            {
                list.Add(new CoopMissionNetworkBridge());
                list.Add(new CoopMissionClientLogic());
#if !COOPSPECTATOR_DEDICATED
                if (ExperimentalFeatures.EnableCustomCoopSelectionOverlay)
                {
                    list.Add(new CoopSpectator.UI.CoopMissionSelectionView());
                    ModLogger.Info("MissionStateOpenNewPatches: appended CoopMissionSelectionView to explicit listed TeamDeathmatch client ingress.");
                }
#endif
                ModLogger.Info("MissionStateOpenNewPatches: appended CoopMissionNetworkBridge to explicit listed TeamDeathmatch client ingress.");
                ModLogger.Info("MissionStateOpenNewPatches: appended CoopMissionClientLogic to explicit listed TeamDeathmatch client ingress.");
            }

            ModLogger.Info(
                "MissionStateOpenNewPatches: explicit listed TeamDeathmatch ingress ready. " +
                "FinalCount=" + list.Count +
                " IsServer=" + GameNetwork.IsServer +
                " Scene=" + (mission?.SceneName ?? "unknown"));
            return list;
        }

        private static List<MissionBehavior> BuildListedTeamDeathmatchServerBehaviors(Mission mission)
        {
            List<MissionBehavior> list = new List<MissionBehavior>();
            AddRequired(list, MissionBehaviorHelpers.TryCreateMissionLobbyComponent(), "MissionLobbyComponent");
            list.Add(new ListedShellCompatibilityMode());
            list.Add(new ListedShellCompatibilityModeClient());
            list.Add(new MultiplayerTimerComponent());

            AddRequired(
                list,
                TryCreateBehaviorByFullNames("TaleWorlds.MountAndBlade.Multiplayer.Missions.MultiplayerBattleMissionAgentInteractionLogic"),
                "MultiplayerBattleMissionAgentInteractionLogic");
            AddRequired(list, MissionBehaviorHelpers.TryCreateHardBorderPlacer(), "MissionHardBorderPlacer");
            AddRequired(list, MissionBehaviorHelpers.TryCreateBoundaryPlacer(), "MissionBoundaryPlacer");
            AddRequired(list, MissionBehaviorHelpers.TryCreateBoundaryCrossingHandler(mission), "MissionBoundaryCrossingHandler");
            list.Add(new MultiplayerAdminComponent());
            AddRequired(list, MissionBehaviorHelpers.TryCreateMissionOptionsComponent(mission), "MissionOptionsComponent");
            AddRequired(list, MissionBehaviorHelpers.TryCreateMissionScoreboardComponent(), "MissionScoreboardComponent");
            list.Add(new MissionAgentPanicHandler());
            list.Add(new AgentHumanAILogic());
            list.Add(new EquipmentControllerLeaveLogic());
            AddRequired(
                list,
                TryCreateBehaviorByFullNames("TaleWorlds.MountAndBlade.MultiplayerPreloadHelper"),
                "MultiplayerPreloadHelper");

            ModLogger.Info("MissionStateOpenNewPatches: assembled explicit listed TeamDeathmatch server ingress without vanilla behavior-list diffing.");
            return list;
        }

        private static List<MissionBehavior> BuildListedTeamDeathmatchClientBehaviors(Mission mission)
        {
            List<MissionBehavior> list = new List<MissionBehavior>();
            AddRequired(list, MissionBehaviorHelpers.TryCreateMissionLobbyComponent(), "MissionLobbyComponent");
            list.Add(new ListedShellCompatibilityModeClient());

            AddOptional(
                list,
                TryCreateBehaviorByFullNames("TaleWorlds.MountAndBlade.MultiplayerAchievementComponent"),
                "MultiplayerAchievementComponent");
            list.Add(new MultiplayerTimerComponent());
            AddRequired(
                list,
                TryCreateBehaviorByFullNames("TaleWorlds.MountAndBlade.Multiplayer.Missions.MultiplayerBattleMissionAgentInteractionLogic"),
                "MultiplayerBattleMissionAgentInteractionLogic");
            AddRequired(list, MissionBehaviorHelpers.TryCreateHardBorderPlacer(), "MissionHardBorderPlacer");
            AddRequired(list, MissionBehaviorHelpers.TryCreateBoundaryPlacer(), "MissionBoundaryPlacer");
            AddRequired(list, MissionBehaviorHelpers.TryCreateBoundaryCrossingHandler(mission), "MissionBoundaryCrossingHandler");
            list.Add(new MultiplayerAdminComponent());
            AddRequired(list, MissionBehaviorHelpers.TryCreateMissionOptionsComponent(mission), "MissionOptionsComponent");
            AddOptional(list, MissionBehaviorHelpers.TryCreateMissionMatchHistoryComponent(), "MissionMatchHistoryComponent");
            list.Add(new EquipmentControllerLeaveLogic());
            AddRequired(
                list,
                TryCreateBehaviorByFullNames("TaleWorlds.MountAndBlade.MultiplayerPreloadHelper"),
                "MultiplayerPreloadHelper");

            ModLogger.Info("MissionStateOpenNewPatches: assembled explicit listed TeamDeathmatch client ingress without vanilla behavior-list diffing.");
            return list;
        }

        private static MissionBehavior TryCreateBehaviorByFullNames(params string[] fullTypeNames)
        {
            if (fullTypeNames == null)
                return null;

            for (int i = 0; i < fullTypeNames.Length; i++)
            {
                string fullTypeName = fullTypeNames[i];
                if (string.IsNullOrWhiteSpace(fullTypeName))
                    continue;

                MissionBehavior behavior =
                    MissionBehaviorHelpers.TryCreateBehavior(fullTypeName)
                    ?? MissionBehaviorHelpers.TryCreateBehaviorFromMountAndBlade(fullTypeName)
                    ?? MissionBehaviorHelpers.TryCreateBehaviorFromLoadedAssemblies(fullTypeName);
                if (behavior != null)
                    return behavior;
            }

            return null;
        }

        private static void AddOptional(List<MissionBehavior> list, MissionBehavior behavior, string behaviorName)
        {
            if (behavior == null)
            {
                ModLogger.Info("MissionStateOpenNewPatches: optional listed-shell behavior unavailable: " + (behaviorName ?? "unknown") + ".");
                return;
            }

            list.Add(behavior);
        }

        private static void AddRequired(List<MissionBehavior> list, MissionBehavior behavior, string behaviorName)
        {
            if (behavior == null)
            {
                ModLogger.Error(
                    "MissionStateOpenNewPatches: required listed-shell behavior unavailable: " + (behaviorName ?? "unknown") + ".",
                    null);
                return;
            }

            list.Add(behavior);
        }
    }
}
