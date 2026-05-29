using System;
using System.Linq;
using System.Reflection;
using CoopSpectator.GameMode;
using CoopSpectator.Infrastructure;
using HarmonyLib;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.Patches
{
    /// <summary>
    /// Fallback interception for legacy native TeamDeathmatch mission open.
    /// The primary listed ingress now enters through the explicit listed shell mode.
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

            if (ListedShellMissionBehaviorFactory.IsFactoryDelegate(handler))
            {
                ModLogger.Info("MissionState.OpenNew: skip listed TeamDeathmatch shell wrapping for explicit listed-shell behavior factory.");
                return;
            }

            handler = ListedShellMissionBehaviorFactory.CreateMissionBehaviors;
            ModLogger.Info("MissionState.OpenNew: fallback-replaced TeamDeathmatch behavior handler with explicit listed-shell coop ingress assembly.");
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
    }
}
