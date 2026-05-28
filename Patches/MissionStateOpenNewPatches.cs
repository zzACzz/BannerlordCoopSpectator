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
    /// Logs MissionState.OpenNew lifecycle and wraps only the listed
    /// TeamDeathmatch shell where the coop runtime still attaches to native MP startup.
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

            InitializeMissionBehaviorsDelegate originalHandler = handler;
            handler = mission => WrapVanillaTeamDeathmatchBehaviors(mission, originalHandler);
            ModLogger.Info("MissionState.OpenNew: wrapped TeamDeathmatch behavior handler for coop runtime injection.");
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
                list.Add(new CoopMissionNetworkBridge());
                list.Add(new CoopMissionSpawnLogic());
                ModLogger.Info("MissionStateOpenNewPatches: appended CoopMissionNetworkBridge to vanilla TeamDeathmatch.");
                ModLogger.Info("MissionStateOpenNewPatches: appended CoopMissionSpawnLogic to vanilla TeamDeathmatch.");
            }
            else
            {
                list.Add(new CoopMissionNetworkBridge());
                list.Add(new CoopMissionClientLogic());
#if !COOPSPECTATOR_DEDICATED
                if (ExperimentalFeatures.EnableCustomCoopSelectionOverlay)
                {
                    list.Add(new CoopSpectator.UI.CoopMissionSelectionView());
                    ModLogger.Info("MissionStateOpenNewPatches: appended CoopMissionSelectionView to wrapped TeamDeathmatch client stack.");
                }
#endif
                ModLogger.Info("MissionStateOpenNewPatches: appended CoopMissionNetworkBridge to vanilla TeamDeathmatch.");
                ModLogger.Info("MissionStateOpenNewPatches: appended CoopMissionClientLogic to vanilla TeamDeathmatch.");
            }

            list.Add(new MissionBehaviorDiagnostic());
            ModLogger.Info("MissionStateOpenNewPatches: appended MissionBehaviorDiagnostic to vanilla TeamDeathmatch. FinalCount=" + list.Count);
            return list;
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

    }
}
