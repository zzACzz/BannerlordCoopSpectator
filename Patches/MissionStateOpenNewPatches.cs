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

            int removedEntryUiCount = list.RemoveAll(ShouldRemoveVanillaEntryBehavior);
            int removedNativeEquipmentBootstrapCount = ReplaceNativeEquipmentBootstrapWithPassiveCompatibilityShell(list);
            if (removedEntryUiCount > 0)
                ModLogger.Info("MissionStateOpenNewPatches: removed vanilla entry behaviors from wrapped TeamDeathmatch stack. RemovedCount=" + removedEntryUiCount);
            if (removedNativeEquipmentBootstrapCount > 0)
            {
                ModLogger.Info(
                    "MissionStateOpenNewPatches: replaced native MissionLobbyEquipmentNetworkComponent in wrapped TeamDeathmatch shell. " +
                    "RemovedCount=" + removedNativeEquipmentBootstrapCount);
            }

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

            ModLogger.Info("MissionStateOpenNewPatches: wrapped TeamDeathmatch shell ready. FinalCount=" + list.Count);
            return list;
        }

        private static bool ShouldRemoveVanillaEntryBehavior(MissionBehavior behavior)
        {
            if (behavior == null)
                return false;

            string fullName = behavior.GetType().FullName ?? string.Empty;
            return fullName.IndexOf("MissionGauntletTeamSelection", StringComparison.OrdinalIgnoreCase) >= 0
                || fullName.IndexOf("MissionGauntletClassLoadout", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static int ReplaceNativeEquipmentBootstrapWithPassiveCompatibilityShell(List<MissionBehavior> list)
        {
            if (list == null || list.Count == 0)
                return 0;

            int removedCount = 0;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                MissionBehavior behavior = list[i];
                if (behavior == null)
                    continue;

                if (!string.Equals(behavior.GetType().Name, nameof(MissionLobbyEquipmentNetworkComponent), StringComparison.Ordinal))
                    continue;

                list.RemoveAt(i);
                removedCount++;
            }

            if (removedCount > 0)
                list.Add(new ListedShellEquipmentCompatibilityComponent());

            return removedCount;
        }

    }
}
