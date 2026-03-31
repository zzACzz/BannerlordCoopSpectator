using System;
using System.Reflection;
using CoopSpectator.GameMode;
using CoopSpectator.Infrastructure;
using CoopSpectator.MissionBehaviors;
using HarmonyLib;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.Patches
{
    /// <summary>
    /// Keeps native Battle/TDM shell behaviors alive for bootstrap compatibility,
    /// but suppresses the warmup/timer loop once the mission is running as our
    /// scene-aware coop battle on mp_battle_map_*.
    /// </summary>
    public static class BattleShellSuppressionPatch
    {
        private static string _lastSuppressionLogKey;

        public static void Apply(Harmony harmony)
        {
            try
            {
                PatchMethod(
                    harmony,
                    "TaleWorlds.MountAndBlade.MultiplayerWarmupComponent",
                    "AfterStart",
                    nameof(MultiplayerWarmupComponent_AfterStart_Prefix));
                PatchMethod(
                    harmony,
                    "TaleWorlds.MountAndBlade.MultiplayerWarmupComponent",
                    "OnPreDisplayMissionTick",
                    nameof(MultiplayerWarmupComponent_OnPreDisplayMissionTick_Prefix),
                    typeof(float));
                PatchMethod(
                    harmony,
                    "TaleWorlds.MountAndBlade.MultiplayerWarmupComponent",
                    "HandleNewClientAfterSynchronized",
                    nameof(MultiplayerWarmupComponent_HandleNewClientAfterSynchronized_Prefix),
                    AccessTools.TypeByName("TaleWorlds.MountAndBlade.NetworkCommunicator"));
                PatchMethod(
                    harmony,
                    "TaleWorlds.MountAndBlade.MultiplayerTimerComponent",
                    "StartTimerAsServer",
                    nameof(MultiplayerTimerComponent_StartTimerAsServer_Prefix),
                    typeof(float));
                PatchMethod(
                    harmony,
                    "TaleWorlds.MountAndBlade.MultiplayerTimerComponent",
                    "StartTimerAsClient",
                    nameof(MultiplayerTimerComponent_StartTimerAsClient_Prefix),
                    typeof(float),
                    typeof(float));
                PatchMethod(
                    harmony,
                    "TaleWorlds.MountAndBlade.Multiplayer.ConsoleMatchStartEndHandler",
                    "OnMissionTick",
                    nameof(ConsoleMatchStartEndHandler_OnMissionTick_Prefix),
                    typeof(float));

                ModLogger.Info("BattleShellSuppressionPatch: native warmup/timer suppression patches applied.");
            }
            catch (Exception ex)
            {
                ModLogger.Error("BattleShellSuppressionPatch.Apply failed.", ex);
            }
        }

        private static void PatchMethod(Harmony harmony, string typeName, string methodName, string prefixMethodName, params Type[] parameterTypes)
        {
            Type targetType = AccessTools.TypeByName(typeName);
            if (targetType == null)
            {
                ModLogger.Info("BattleShellSuppressionPatch: type not found. Type=" + typeName);
                return;
            }

            MethodInfo target = parameterTypes == null || parameterTypes.Length == 0
                ? targetType.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                : targetType.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, parameterTypes, null);
            MethodInfo prefix = typeof(BattleShellSuppressionPatch).GetMethod(prefixMethodName, BindingFlags.Static | BindingFlags.NonPublic);
            if (target == null || prefix == null)
            {
                ModLogger.Info("BattleShellSuppressionPatch: method not found. Type=" + typeName + " Method=" + methodName);
                return;
            }

            harmony.Patch(target, prefix: new HarmonyMethod(prefix));
            ModLogger.Info("BattleShellSuppressionPatch: patched " + typeName + "." + methodName + ".");
        }

        private static bool MultiplayerWarmupComponent_AfterStart_Prefix(object __instance)
        {
            return !ShouldSuppressNativeBattleShell(__instance, "MultiplayerWarmupComponent.AfterStart");
        }

        private static bool MultiplayerWarmupComponent_OnPreDisplayMissionTick_Prefix(object __instance, float dt)
        {
            return !ShouldSuppressNativeBattleShell(__instance, "MultiplayerWarmupComponent.OnPreDisplayMissionTick");
        }

        private static bool MultiplayerWarmupComponent_HandleNewClientAfterSynchronized_Prefix(object __instance, object networkPeer)
        {
            return !ShouldSuppressNativeBattleShell(__instance, "MultiplayerWarmupComponent.HandleNewClientAfterSynchronized");
        }

        private static bool MultiplayerTimerComponent_StartTimerAsServer_Prefix(object __instance, float duration)
        {
            return !ShouldSuppressNativeBattleShell(__instance, "MultiplayerTimerComponent.StartTimerAsServer");
        }

        private static bool MultiplayerTimerComponent_StartTimerAsClient_Prefix(object __instance, float startTime, float duration)
        {
            return !ShouldSuppressNativeBattleShell(__instance, "MultiplayerTimerComponent.StartTimerAsClient");
        }

        private static bool ConsoleMatchStartEndHandler_OnMissionTick_Prefix(object __instance, float dt)
        {
            return !ShouldSuppressNativeBattleShell(__instance, "ConsoleMatchStartEndHandler.OnMissionTick");
        }

        private static bool ShouldSuppressNativeBattleShell(object instance, string source)
        {
            Mission mission = (instance as MissionBehavior)?.Mission ?? Mission.Current;
            if (!IsCoopBattleMapRuntime(mission))
                return false;

            string key = (source ?? "unknown") + "|" + (mission?.SceneName ?? "unknown");
            if (!string.Equals(_lastSuppressionLogKey, key, StringComparison.Ordinal))
            {
                _lastSuppressionLogKey = key;
                ModLogger.Info(
                    "BattleShellSuppressionPatch: suppressed native battle shell path. " +
                    "Source=" + (source ?? "unknown") +
                    " Scene=" + (mission?.SceneName ?? "unknown") +
                    " HasCoopBattleServer=" + (mission?.GetMissionBehavior<MissionMultiplayerCoopBattle>() != null) +
                    " HasCoopBattleClient=" + (mission?.GetMissionBehavior<MissionMultiplayerCoopBattleClient>() != null) +
                    " HasCoopSpawnLogic=" + (mission?.GetMissionBehavior<CoopMissionSpawnLogic>() != null) + ".");
            }

            return true;
        }

        private static bool IsCoopBattleMapRuntime(Mission mission)
        {
            if (mission == null)
                return false;

            string sceneName = mission.SceneName ?? string.Empty;
            if (!SceneRuntimeClassifier.IsSceneAwareBattleRuntimeScene(sceneName))
                return false;

            return mission.GetMissionBehavior<MissionMultiplayerCoopBattle>() != null
                || mission.GetMissionBehavior<MissionMultiplayerCoopBattleClient>() != null
                || mission.GetMissionBehavior<CoopMissionSpawnLogic>() != null;
        }
    }
}
