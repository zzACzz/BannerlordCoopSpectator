using System;
using System.Reflection;
using HarmonyLib;
using CoopSpectator.GameMode;
using CoopSpectator.Infrastructure;
using CoopSpectator.MissionBehaviors;
using CoopSpectator.UI;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.Patches
{
    /// <summary>
    /// Temporary client-side guard for vanilla custom-game intermission VM.
    /// The current TdmClone lobby flow triggers exceptions on culture-vote updates
    /// before LoadMission; swallowing them lets us verify whether this is the
    /// remaining client crash source.
    /// </summary>
    public static class IntermissionVmCrashGuardPatch
    {
        private static string _lastSuppressionLogKey;

        public static void Apply(Harmony harmony)
        {
            try
            {
                Type vmType = AccessTools.TypeByName("TaleWorlds.MountAndBlade.Multiplayer.ViewModelCollection.Intermission.MPIntermissionVM");
                if (vmType == null)
                {
                    ModLogger.Info("IntermissionVmCrashGuardPatch: MPIntermissionVM type not found. Skip.");
                    return;
                }

                PatchFinalizer(harmony, vmType, "OnIntermissionStateUpdated");
                PatchFinalizer(harmony, vmType, "OnPlayerVotedForCulture");
                PatchFinalizer(harmony, vmType, "OnPlayerVotedForMap");
            }
            catch (Exception ex)
            {
                ModLogger.Error("IntermissionVmCrashGuardPatch.Apply failed.", ex);
            }
        }

        private static void PatchFinalizer(Harmony harmony, Type vmType, string methodName)
        {
            MethodInfo target = AccessTools.Method(vmType, methodName);
            if (target == null)
            {
                ModLogger.Info("IntermissionVmCrashGuardPatch: " + methodName + " not found on MPIntermissionVM. Skip.");
                return;
            }

            MethodInfo prefix = typeof(IntermissionVmCrashGuardPatch).GetMethod(
                nameof(SuppressCoopBattleMapIntermissionVm),
                BindingFlags.Static | BindingFlags.NonPublic);
            MethodInfo finalizer = typeof(IntermissionVmCrashGuardPatch).GetMethod(
                nameof(SwallowIntermissionVmException),
                BindingFlags.Static | BindingFlags.NonPublic);

            harmony.Patch(
                target,
                prefix: new HarmonyMethod(prefix),
                finalizer: new HarmonyMethod(finalizer));
            ModLogger.Info("IntermissionVmCrashGuardPatch: applied prefix/finalizer to MPIntermissionVM." + methodName);
        }

        private static bool SuppressCoopBattleMapIntermissionVm(MethodBase __originalMethod)
        {
            if (!ShouldSuppressForCoopBattleMap())
                return true;

            string currentScene = Mission.Current?.SceneName ?? "null";
            string phase = CoopBattlePhaseBridgeFile.ReadStatus()?.Phase.ToString() ?? string.Empty;
            string readinessStage = CoopBattleEntryStatusBridgeFile.ReadStatus()?.BattleDataReadinessStage ?? string.Empty;
            string logKey = string.Join("|", new[]
            {
                __originalMethod?.Name ?? "unknown",
                currentScene,
                phase,
                readinessStage,
                GameNetwork.IsSessionActive.ToString()
            });
            if (!string.Equals(_lastSuppressionLogKey, logKey, StringComparison.Ordinal))
            {
                _lastSuppressionLogKey = logKey;
                ModLogger.Info(
                    "IntermissionVmCrashGuardPatch: suppressed vanilla intermission VM callback. " +
                    "Method=" + (__originalMethod?.Name ?? "unknown") +
                    " Scene=" + currentScene +
                    " Phase=" + phase +
                    " ReadinessStage=" + readinessStage +
                    " SessionActive=" + GameNetwork.IsSessionActive);
            }
            return false;
        }

        private static bool ShouldSuppressForCoopBattleMap()
        {
            if (!ExperimentalFeatures.EnableCustomCoopSelectionOverlay)
                return false;

            if (!GameNetwork.IsClient || !GameNetwork.IsSessionActive)
                return false;

            Mission mission = Mission.Current;
            if (mission != null)
                return MissionMultiplayerCoopBattleMode.IsBattleMapSceneName(mission.SceneName);

            // In coop battle-map flow the vanilla intermission VM starts receiving callbacks
            // before the battle-map mission and custom selection shell are fully materialized.
            // At that stage we already want our custom overlay to own selection, so keep the
            // vanilla intermission VM inert for the whole active coop MP session.
            return true;
        }

        private static Exception SwallowIntermissionVmException(Exception __exception, MethodBase __originalMethod)
        {
            if (__exception == null)
                return null;

            ModLogger.Error("IntermissionVmCrashGuardPatch: swallowed exception in " + (__originalMethod?.DeclaringType?.FullName ?? "unknown") + "." + (__originalMethod?.Name ?? "unknown"), __exception);
            return null;
        }
    }
}
