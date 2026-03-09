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
            MissionInitializerRecord rec,
            ref InitializeMissionBehaviorsDelegate handler,
            bool addDefaultMissionBehaviors,
            bool needsMemoryCleanup)
        {
            ModLogger.Info("MissionState.OpenNew ENTER missionName=" + (missionName ?? "") + " (engine will create mission then call behavior factory).");
            if (!ShouldInjectDiagnostics(missionName))
                return;

            InitializeMissionBehaviorsDelegate originalHandler = handler;
            handler = mission => WrapVanillaTeamDeathmatchBehaviors(mission, originalHandler);
            ModLogger.Info("MissionState.OpenNew: wrapped TeamDeathmatch behavior handler for passive diagnostics injection.");
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
            return ExperimentalFeatures.EnableVanillaTeamDeathmatchDiagnosticsInjection
                && !ExperimentalFeatures.EnableTdmCloneExperiment
                && string.Equals(missionName, OfficialTeamDeathmatchMissionName, StringComparison.Ordinal);
        }

        private static IEnumerable<MissionBehavior> WrapVanillaTeamDeathmatchBehaviors(
            Mission mission,
            InitializeMissionBehaviorsDelegate originalHandler)
        {
            List<MissionBehavior> list = originalHandler != null
                ? new List<MissionBehavior>(originalHandler(mission) ?? Enumerable.Empty<MissionBehavior>())
                : new List<MissionBehavior>();

            if (GameNetwork.IsServer)
            {
                list.Add(new MissionMinimalServerDiagnosticMode());
                list.Add(new CoopMissionSpawnLogic());
                ModLogger.Info("MissionStateOpenNewPatches: appended MissionMinimalServerDiagnosticMode to vanilla TeamDeathmatch.");
                ModLogger.Info("MissionStateOpenNewPatches: appended CoopMissionSpawnLogic to vanilla TeamDeathmatch.");
            }
            else
            {
                list.Add(new MissionMinimalClientDiagnosticMode());
                list.Add(new CoopMissionClientLogic());
                ModLogger.Info("MissionStateOpenNewPatches: appended MissionMinimalClientDiagnosticMode to vanilla TeamDeathmatch.");
                ModLogger.Info("MissionStateOpenNewPatches: appended CoopMissionClientLogic to vanilla TeamDeathmatch.");
            }

            list.Add(new MissionBehaviorDiagnostic());
            ModLogger.Info("MissionStateOpenNewPatches: appended MissionBehaviorDiagnostic to vanilla TeamDeathmatch. FinalCount=" + list.Count);
            return list;
        }
    }
}
