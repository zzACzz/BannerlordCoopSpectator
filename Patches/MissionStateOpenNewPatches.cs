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
    /// Logs MissionState.OpenNew lifecycle and, in the stable baseline, wraps the
    /// vanilla TeamDeathmatch behavior factory to append passive diagnostics only.
    /// </summary>
    public static class MissionStateOpenNewPatches
    {
        private const string OfficialTeamDeathmatchMissionName = "MultiplayerTeamDeathmatch";
        private const string OfficialBattleMissionName = "MultiplayerBattle";

        public static void Apply(Harmony harmony)
        {
            try
            {
                BattleMapContractDiagnostics.LogMissionStateOpenNewContract("MissionStateOpenNewPatches.Apply");
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
                MethodInfo postfix = typeof(MissionStateOpenNewPatches).GetMethod(nameof(OpenNew_Postfix), BindingFlags.Public | BindingFlags.Static);
                if (prefix == null || postfix == null)
                {
                    ModLogger.Info("MissionStateOpenNewPatches: prefix/postfix methods not found. Skip.");
                    return;
                }

                harmony.Patch(openNew, prefix: new HarmonyMethod(prefix), postfix: new HarmonyMethod(postfix));
                ModLogger.Info("MissionStateOpenNewPatches: OpenNew prefix/postfix applied (TeamDeathmatch diagnostic injection ready).");
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
            ModLogger.Info("MissionState.OpenNew ENTER missionName=" + (missionName ?? "") + " (engine will create mission then call behavior factory).");
            BattleMapContractDiagnostics.LogMissionInitializerRecordState(rec, "MissionState.OpenNew prefix");
            LogMissionOpenHandlerContract(missionName, handler, addDefaultMissionBehaviors, needsMemoryCleanup);

            bool isOfficialBattleMission = string.Equals(missionName, OfficialBattleMissionName, StringComparison.Ordinal);
            bool isCoopBattleFactory = IsCoopBattleBehaviorFactory(handler);
            if (isOfficialBattleMission && !isCoopBattleFactory)
            {
                string runtimeScene = rec.SceneName ?? string.Empty;
                CampaignMapPatchMissionInit.TryApply(ref rec, runtimeScene, "MissionState.OpenNew Battle");
            }

            if (!ShouldInjectDiagnostics(missionName))
                return;

            if (isCoopBattleFactory)
            {
                ModLogger.Info("MissionState.OpenNew: skip vanilla TeamDeathmatch diagnostic wrapping for CoopBattle custom behavior factory.");
                return;
            }

            InitializeMissionBehaviorsDelegate originalHandler = handler;
            if (string.Equals(missionName, OfficialTeamDeathmatchMissionName, StringComparison.Ordinal))
            {
                handler = mission => WrapVanillaTeamDeathmatchBehaviors(mission, originalHandler);
                ModLogger.Info("MissionState.OpenNew: wrapped TeamDeathmatch behavior handler for passive diagnostics injection.");
            }
            else if (string.Equals(missionName, OfficialBattleMissionName, StringComparison.Ordinal) && GameNetwork.IsClient)
            {
                handler = mission => WrapVanillaBattleClientBehaviors(mission, originalHandler);
                ModLogger.Info("MissionState.OpenNew: wrapped Battle client behavior handler for coop selection overlay injection.");
            }
        }

        public static void OpenNew_Postfix(
            string missionName,
            MissionInitializerRecord rec,
            InitializeMissionBehaviorsDelegate handler,
            bool addDefaultMissionBehaviors,
            bool needsMemoryCleanup)
        {
            ModLogger.Info("MissionState.OpenNew EXIT missionName=" + (missionName ?? "") + " (original method returned).");
        }

        private static bool ShouldInjectDiagnostics(string missionName)
        {
            if (!ExperimentalFeatures.EnableVanillaTeamDeathmatchDiagnosticsInjection || ExperimentalFeatures.EnableTdmCloneExperiment)
                return false;

            return string.Equals(missionName, OfficialTeamDeathmatchMissionName, StringComparison.Ordinal)
                || (GameNetwork.IsClient && string.Equals(missionName, OfficialBattleMissionName, StringComparison.Ordinal));
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

        private static void LogMissionOpenHandlerContract(
            string missionName,
            InitializeMissionBehaviorsDelegate handler,
            bool addDefaultMissionBehaviors,
            bool needsMemoryCleanup)
        {
            try
            {
                if (!string.Equals(missionName, OfficialBattleMissionName, StringComparison.Ordinal) &&
                    !string.Equals(missionName, OfficialTeamDeathmatchMissionName, StringComparison.Ordinal))
                    return;

                MethodInfo handlerMethod = handler?.Method;
                Type declaringType = handlerMethod?.DeclaringType;
                Type targetType = handler?.Target?.GetType();
                ModLogger.Info(
                    "MissionState.OpenNew handler contract. " +
                    "MissionName=" + (missionName ?? string.Empty) +
                    " HandlerMethod=" + (handlerMethod?.Name ?? "null") +
                    " HandlerDeclaringType=" + (declaringType?.FullName ?? "null") +
                    " HandlerTargetType=" + (targetType?.FullName ?? "null") +
                    " AddDefaultMissionBehaviors=" + addDefaultMissionBehaviors +
                    " NeedsMemoryCleanup=" + needsMemoryCleanup +
                    " IsServer=" + GameNetwork.IsServer +
                    " IsClient=" + GameNetwork.IsClient + ".");
            }
            catch (Exception ex)
            {
                ModLogger.Info("MissionState.OpenNew handler contract log failed: " + ex.Message);
            }
        }

        private static IEnumerable<MissionBehavior> WrapVanillaTeamDeathmatchBehaviors(
            Mission mission,
            InitializeMissionBehaviorsDelegate originalHandler)
        {
            List<MissionBehavior> list = originalHandler != null
                ? new List<MissionBehavior>(originalHandler(mission) ?? Enumerable.Empty<MissionBehavior>())
                : new List<MissionBehavior>();

            LogWrappedBehaviorStack("before-removal", list);

            int removedEntryUiCount = list.RemoveAll(ShouldRemoveVanillaEntryBehavior);
            if (removedEntryUiCount > 0)
                ModLogger.Info("MissionStateOpenNewPatches: removed vanilla entry behaviors from wrapped TeamDeathmatch stack. RemovedCount=" + removedEntryUiCount);
            else
                ModLogger.Info("MissionStateOpenNewPatches: no vanilla entry behaviors matched removal filter in wrapped TeamDeathmatch stack.");

            LogWrappedBehaviorStack("after-removal", list);

            if (GameNetwork.IsServer)
            {
                list.Add(new MissionMinimalServerDiagnosticMode());
                list.Add(new CoopMissionNetworkBridge());
                list.Add(new CoopMissionSpawnLogic());
                ModLogger.Info("MissionStateOpenNewPatches: appended MissionMinimalServerDiagnosticMode to vanilla TeamDeathmatch.");
                ModLogger.Info("MissionStateOpenNewPatches: appended CoopMissionNetworkBridge to vanilla TeamDeathmatch.");
                ModLogger.Info("MissionStateOpenNewPatches: appended CoopMissionSpawnLogic to vanilla TeamDeathmatch.");
            }
            else
            {
                list.Add(new MissionMinimalClientDiagnosticMode());
                list.Add(new CoopMissionNetworkBridge());
                list.Add(new CoopMissionClientLogic());
#if !COOPSPECTATOR_DEDICATED
                if (ExperimentalFeatures.EnableCustomCoopSelectionOverlay)
                {
                    list.Add(new CoopSpectator.UI.CoopMissionSelectionView());
                    ModLogger.Info("MissionStateOpenNewPatches: appended CoopMissionSelectionView to wrapped TeamDeathmatch client stack.");
                }
#endif
                ModLogger.Info("MissionStateOpenNewPatches: appended MissionMinimalClientDiagnosticMode to vanilla TeamDeathmatch.");
                ModLogger.Info("MissionStateOpenNewPatches: appended CoopMissionNetworkBridge to vanilla TeamDeathmatch.");
                ModLogger.Info("MissionStateOpenNewPatches: appended CoopMissionClientLogic to vanilla TeamDeathmatch.");
            }

            list.Add(new MissionBehaviorDiagnostic());
            ModLogger.Info("MissionStateOpenNewPatches: appended MissionBehaviorDiagnostic to vanilla TeamDeathmatch. FinalCount=" + list.Count);
            return list;
        }

        private static IEnumerable<MissionBehavior> WrapVanillaBattleClientBehaviors(
            Mission mission,
            InitializeMissionBehaviorsDelegate originalHandler)
        {
            List<MissionBehavior> list = originalHandler != null
                ? new List<MissionBehavior>(originalHandler(mission) ?? Enumerable.Empty<MissionBehavior>())
                : new List<MissionBehavior>();
            bool battleMapRuntime = MissionMultiplayerCoopBattleMode.IsBattleMapSceneName(mission?.SceneName ?? string.Empty);

            LogWrappedBehaviorStack("battle-before-removal", list);

            int removedEntryUiCount = list.RemoveAll(ShouldRemoveVanillaEntryBehavior);
            if (removedEntryUiCount > 0)
                ModLogger.Info("MissionStateOpenNewPatches: removed vanilla entry gauntlet behaviors from wrapped Battle client stack. RemovedCount=" + removedEntryUiCount);
            else
                ModLogger.Info("MissionStateOpenNewPatches: no vanilla entry gauntlet behaviors matched removal filter in wrapped Battle client stack.");

            if (battleMapRuntime && !ExperimentalFeatures.EnableBattleMapClientEquipmentNetworkComponent)
            {
                int removedEquipmentNetworkCount = list.RemoveAll(ShouldRemoveBattleClientEquipmentNetworkBehavior);
                if (removedEquipmentNetworkCount > 0)
                    ModLogger.Info("MissionStateOpenNewPatches: removed MissionLobbyEquipmentNetworkComponent from wrapped Battle client stack for battle-map spawn crash isolation. RemovedCount=" + removedEquipmentNetworkCount);
                else
                    ModLogger.Info("MissionStateOpenNewPatches: MissionLobbyEquipmentNetworkComponent not found in wrapped Battle client stack during battle-map spawn crash isolation.");
            }

            LogWrappedBehaviorStack("battle-after-removal", list);

            if (battleMapRuntime)
            {
                InjectBattleMapClientUiParityViews(mission, list);
            }

#if !COOPSPECTATOR_DEDICATED
            if (ExperimentalFeatures.EnableCustomCoopSelectionOverlay)
            {
                list.Add(new CoopMissionNetworkBridge());
                ModLogger.Info("MissionStateOpenNewPatches: appended CoopMissionNetworkBridge to wrapped Battle client stack.");
                list.Add(new CoopSpectator.UI.CoopMissionSelectionView());
                ModLogger.Info("MissionStateOpenNewPatches: appended CoopMissionSelectionView to wrapped Battle client stack.");
            }
#endif
            list.Add(new MissionBehaviorDiagnostic());
            ModLogger.Info("MissionStateOpenNewPatches: appended MissionBehaviorDiagnostic to wrapped Battle client stack. FinalCount=" + list.Count);
            return list;
        }

        private static void InjectBattleMapClientUiParityViews(Mission mission, List<MissionBehavior> list)
        {
            bool addedAgentLabel = TryAddBehaviorIfMissing(
                list,
                () => MissionBehaviorHelpers.TryCreateMissionAgentLabelUiParityView(mission),
                new[] { "MissionAgentLabelUIHandler", "MissionAgentLabelView" },
                "MissionStateOpenNewPatches: battle-map client injected agent-label mission view into wrapped Battle stack.",
                "MissionStateOpenNewPatches: battle-map client already had agent-label mission view in wrapped Battle stack.");

            bool addedFormationTargetSelection = TryAddBehaviorIfMissing(
                list,
                () => MissionBehaviorHelpers.TryCreateBehaviorFromLoadedAssemblies("TaleWorlds.MountAndBlade.View.MissionViews.MissionFormationTargetSelectionHandler"),
                new[] { "MissionFormationTargetSelectionHandler" },
                "MissionStateOpenNewPatches: battle-map client injected MissionFormationTargetSelectionHandler into wrapped Battle stack.",
                "MissionStateOpenNewPatches: battle-map client already had MissionFormationTargetSelectionHandler in wrapped Battle stack.");

            bool addedFormationMarker = TryAddBehaviorIfMissing(
                list,
                () => MissionBehaviorHelpers.TryCreateMissionFormationMarkerUiParityView(mission),
                new[] { "MissionFormationMarkerUIHandler", "MissionGauntletFormationMarker" },
                "MissionStateOpenNewPatches: battle-map client injected formation-marker mission view into wrapped Battle stack.",
                "MissionStateOpenNewPatches: battle-map client already had formation-marker mission view in wrapped Battle stack.");

            ModLogger.Info(
                "CoopBattle client: injected agent label and formation marker mission views for wrapped MultiplayerBattle battle-map runtime. Scene=" + (mission?.SceneName ?? "null") +
                " AddedAgentLabel=" + addedAgentLabel +
                " AddedFormationTargetSelection=" + addedFormationTargetSelection +
                " AddedFormationMarker=" + addedFormationMarker);
        }

        private static bool TryAddBehaviorIfMissing(
            List<MissionBehavior> list,
            Func<MissionBehavior> behaviorFactory,
            string[] expectedTypeNames,
            string addedLogMessage,
            string alreadyPresentLogMessage)
        {
            if (ListContainsBehaviorType(list, expectedTypeNames))
            {
                ModLogger.Info(alreadyPresentLogMessage);
                return false;
            }

            MissionBehavior behavior = behaviorFactory?.Invoke();
            if (behavior == null)
                return false;

            list.Add(behavior);
            ModLogger.Info(addedLogMessage + " AddedType=" + (behavior.GetType().FullName ?? behavior.GetType().Name));
            return true;
        }

        private static bool ListContainsBehaviorType(List<MissionBehavior> list, IEnumerable<string> expectedTypeNames)
        {
            if (list == null || expectedTypeNames == null)
                return false;

            HashSet<string> expected = new HashSet<string>(
                expectedTypeNames.Where(name => !string.IsNullOrEmpty(name)),
                StringComparer.Ordinal);
            if (expected.Count == 0)
                return false;

            for (int i = 0; i < list.Count; i++)
            {
                MissionBehavior behavior = list[i];
                if (behavior == null)
                    continue;

                string typeName = behavior.GetType().Name ?? string.Empty;
                if (expected.Contains(typeName))
                    return true;
            }

            return false;
        }

        private static void LogWrappedBehaviorStack(string stage, List<MissionBehavior> list)
        {
            try
            {
                if (list == null)
                {
                    ModLogger.Info("MissionStateOpenNewPatches: wrapped TeamDeathmatch stack " + stage + " = <null>");
                    return;
                }

                ModLogger.Info("MissionStateOpenNewPatches: wrapped TeamDeathmatch stack " + stage + " count=" + list.Count);
                for (int i = 0; i < list.Count; i++)
                {
                    MissionBehavior behavior = list[i];
                    string typeName = behavior?.GetType().FullName ?? "<null>";
                    ModLogger.Info("MissionStateOpenNewPatches: [" + stage + ":" + i + "] " + typeName);
                }
            }
            catch (Exception ex)
            {
                ModLogger.Info("MissionStateOpenNewPatches: failed to log wrapped TeamDeathmatch stack " + stage + ": " + ex.Message);
            }
        }

        private static bool ShouldRemoveVanillaEntryBehavior(MissionBehavior behavior)
        {
            if (behavior == null)
                return false;

            string fullName = behavior.GetType().FullName ?? string.Empty;
            return fullName.IndexOf("MissionGauntletTeamSelection", StringComparison.OrdinalIgnoreCase) >= 0
                || fullName.IndexOf("MissionGauntletClassLoadout", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool ShouldRemoveBattleClientEquipmentNetworkBehavior(MissionBehavior behavior)
        {
            if (behavior == null)
                return false;

            string fullName = behavior.GetType().FullName ?? string.Empty;
            return fullName.IndexOf("MissionLobbyEquipmentNetworkComponent", StringComparison.OrdinalIgnoreCase) >= 0;
        }

    }
}
