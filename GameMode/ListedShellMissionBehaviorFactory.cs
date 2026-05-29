using System;
using System.Collections.Generic;
using CoopSpectator.Infrastructure;
using CoopSpectator.MissionBehaviors;
using CoopSpectator.Patches;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.GameMode
{
    /// <summary>
    /// Explicit listed-ingress mission stack shared by the direct listed startup path
    /// and the legacy MissionState.OpenNew fallback interception.
    /// </summary>
    internal static class ListedShellMissionBehaviorFactory
    {
        internal static IEnumerable<MissionBehavior> CreateMissionBehaviors(Mission mission)
        {
            List<MissionBehavior> list = GameNetwork.IsServer
                ? BuildListedTeamDeathmatchServerBehaviors(mission)
                : BuildListedTeamDeathmatchClientBehaviors(mission);

            if (GameNetwork.IsServer)
            {
                list.Add(new CoopMissionNetworkBridge());
                list.Add(new CoopMissionSpawnLogic());
                ModLogger.Info("ListedShellMissionBehaviorFactory: appended CoopMissionNetworkBridge to explicit listed TeamDeathmatch server ingress.");
                ModLogger.Info("ListedShellMissionBehaviorFactory: appended CoopMissionSpawnLogic to explicit listed TeamDeathmatch server ingress.");
            }
            else
            {
                list.Add(new CoopMissionNetworkBridge());
                list.Add(new CoopMissionClientLogic());
#if !COOPSPECTATOR_DEDICATED
                if (ExperimentalFeatures.EnableCustomCoopSelectionOverlay)
                {
                    list.Add(new CoopSpectator.UI.CoopMissionSelectionView());
                    ModLogger.Info("ListedShellMissionBehaviorFactory: appended CoopMissionSelectionView to explicit listed TeamDeathmatch client ingress.");
                }
#endif
                ModLogger.Info("ListedShellMissionBehaviorFactory: appended CoopMissionNetworkBridge to explicit listed TeamDeathmatch client ingress.");
                ModLogger.Info("ListedShellMissionBehaviorFactory: appended CoopMissionClientLogic to explicit listed TeamDeathmatch client ingress.");
            }

            ModLogger.Info(
                "ListedShellMissionBehaviorFactory: explicit listed TeamDeathmatch ingress ready. " +
                "FinalCount=" + list.Count +
                " IsServer=" + GameNetwork.IsServer +
                " Scene=" + (mission?.SceneName ?? "unknown"));
            return list;
        }

        internal static bool IsFactoryDelegate(InitializeMissionBehaviorsDelegate handler)
        {
            if (handler == null)
                return false;

            return handler.Method?.DeclaringType == typeof(ListedShellMissionBehaviorFactory);
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

            ModLogger.Info("ListedShellMissionBehaviorFactory: assembled explicit listed TeamDeathmatch server ingress without vanilla behavior-list diffing.");
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

            ModLogger.Info("ListedShellMissionBehaviorFactory: assembled explicit listed TeamDeathmatch client ingress without vanilla behavior-list diffing.");
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
                ModLogger.Info("ListedShellMissionBehaviorFactory: optional listed-shell behavior unavailable: " + (behaviorName ?? "unknown") + ".");
                return;
            }

            list.Add(behavior);
        }

        private static void AddRequired(List<MissionBehavior> list, MissionBehavior behavior, string behaviorName)
        {
            if (behavior == null)
            {
                ModLogger.Error(
                    "ListedShellMissionBehaviorFactory: required listed-shell behavior unavailable: " + (behaviorName ?? "unknown") + ".",
                    null);
                return;
            }

            list.Add(behavior);
        }
    }
}
