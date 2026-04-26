using System;
using System.Diagnostics;
using System.Collections.Generic;
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
        private static string _lastEndTransitionPassThroughLogKey;
        private static string _lastWarmupAfterStartObservationKey;
        private static string _lastOfficialBattleStartupObservationKey;
        private static string _lastFinishMissionLoadingObservationKey;
        private static string _lastMissionLoadingTickObservationKey;
        private static string _lastEarlyMissionLoadingObservationKey;
        private static string _lastEngineCleanupObservationKey;
        private static string _lastTickLoadingObservationKey;
        private static string _lastIsLoadingFinishedObservationKey;
        private static string _lastMissionStateLoaderObservationKey;
        private static string _lastMissionCurrentStateSetObservationKey;
        private static string _lastClearUnreferencedResourcesSkipObservationKey;
        private static string _lastMissionScreenPreLoadSkipObservationKey;
        private static string _lastMissionScreenPreLoadLoopSkipObservationKey;
        private static string _lastMissionScreenPreLoadEntryObservationKey;
        private static string _lastMissionBehaviorStackObservationKey;
        private static string _lastDedicatedManualLoadMissionStepKey;
        private static string _lastDedicatedManualOnTickStepKey;
        private static readonly HashSet<string> _patchedMissionScreenPreLoadMethods = new HashSet<string>(StringComparer.Ordinal);
        private static Harmony _runtimeHarmony;

        public static void Apply(Harmony harmony)
        {
            _runtimeHarmony = harmony;
            int patchedCount = 0;
            patchedCount += TryPatchMethod(
                harmony,
                "TaleWorlds.MountAndBlade.MissionState",
                "OnActivate",
                nameof(MissionState_OnActivate_Prefix)) ? 1 : 0;
            patchedCount += TryPatchMethod(
                harmony,
                "TaleWorlds.MountAndBlade.MissionState",
                "OnTick",
                nameof(MissionState_OnTick_Prefix),
                typeof(float)) ? 1 : 0;
            patchedCount += TryPatchMethod(
                harmony,
                "TaleWorlds.MountAndBlade.MissionState",
                "LoadMission",
                nameof(MissionState_LoadMission_Prefix)) ? 1 : 0;
            patchedCount += TryPatchMethod(
                harmony,
                "TaleWorlds.MountAndBlade.MissionState",
                "LoadMission",
                nameof(MissionState_LoadMission_Postfix)) ? 1 : 0;
            patchedCount += TryPatchMethod(
                harmony,
                "TaleWorlds.MountAndBlade.MissionState",
                "TickLoading",
                nameof(MissionState_TickLoading_Prefix),
                typeof(float)) ? 1 : 0;
            patchedCount += TryPatchMethod(
                harmony,
                "TaleWorlds.MountAndBlade.MissionState",
                "TickLoading",
                nameof(MissionState_TickLoading_Postfix),
                typeof(float)) ? 1 : 0;
            patchedCount += TryPatchMethod(
                harmony,
                "TaleWorlds.MountAndBlade.Mission",
                "OnMissionStateActivate",
                nameof(Mission_OnMissionStateActivate_Prefix)) ? 1 : 0;
            patchedCount += TryPatchMethod(
                harmony,
                "TaleWorlds.MountAndBlade.Mission",
                "Initialize",
                nameof(Mission_Initialize_Prefix)) ? 1 : 0;
            patchedCount += TryPatchMethod(
                harmony,
                "TaleWorlds.MountAndBlade.Mission",
                "Initialize",
                nameof(Mission_Initialize_Postfix)) ? 1 : 0;
            patchedCount += TryPatchMethod(
                harmony,
                "TaleWorlds.MountAndBlade.Mission",
                "set_CurrentState",
                nameof(Mission_set_CurrentState_Prefix),
                AccessTools.TypeByName("TaleWorlds.MountAndBlade.Mission+State")) ? 1 : 0;
            patchedCount += TryPatchMethod(
                harmony,
                "TaleWorlds.MountAndBlade.Mission",
                "get_IsLoadingFinished",
                nameof(Mission_get_IsLoadingFinished_Prefix)) ? 1 : 0;
            patchedCount += TryPatchMethod(
                harmony,
                "TaleWorlds.MountAndBlade.Mission",
                "get_IsLoadingFinished",
                nameof(Mission_get_IsLoadingFinished_Postfix)) ? 1 : 0;
            patchedCount += TryPatchMethod(
                harmony,
                "TaleWorlds.MountAndBlade.Mission",
                "ClearUnreferencedResources",
                nameof(Mission_ClearUnreferencedResources_Prefix),
                typeof(bool)) ? 1 : 0;
            patchedCount += TryPatchMethod(
                harmony,
                "TaleWorlds.MountAndBlade.Mission",
                "ClearUnreferencedResources",
                nameof(Mission_ClearUnreferencedResources_Postfix),
                typeof(bool)) ? 1 : 0;
            patchedCount += TryPatchMethod(
                harmony,
                "TaleWorlds.MountAndBlade.MissionBehavior",
                "OnMissionScreenPreLoad",
                nameof(MissionBehavior_OnMissionScreenPreLoad_Prefix)) ? 1 : 0;
            patchedCount += TryPatchMethod(
                harmony,
                "TaleWorlds.Engine.Utilities",
                "ClearOldResourcesAndObjects",
                nameof(Utilities_ClearOldResourcesAndObjects_Prefix)) ? 1 : 0;
            patchedCount += TryPatchMethod(
                harmony,
                "TaleWorlds.Engine.Utilities",
                "ClearOldResourcesAndObjects",
                nameof(Utilities_ClearOldResourcesAndObjects_Postfix)) ? 1 : 0;
            patchedCount += TryPatchMethod(
                harmony,
                "TaleWorlds.MountAndBlade.Mission",
                "AfterStart",
                nameof(Mission_AfterStart_Prefix)) ? 1 : 0;
            patchedCount += TryPatchMethod(
                harmony,
                "TaleWorlds.MountAndBlade.MissionState",
                "FinishMissionLoading",
                nameof(MissionState_FinishMissionLoading_Prefix)) ? 1 : 0;
            patchedCount += TryPatchMethod(
                harmony,
                "TaleWorlds.MountAndBlade.Mission",
                "Tick",
                nameof(Mission_Tick_Prefix),
                typeof(float)) ? 1 : 0;
            patchedCount += TryPatchMethod(
                harmony,
                "TaleWorlds.MountAndBlade.MissionLobbyComponent",
                "AfterStart",
                nameof(MissionLobbyComponent_AfterStart_Prefix)) ? 1 : 0;
            patchedCount += TryPatchMethod(
                harmony,
                "TaleWorlds.MountAndBlade.MultiplayerRoundController",
                "AfterStart",
                nameof(MultiplayerRoundController_AfterStart_Prefix)) ? 1 : 0;
            patchedCount += TryPatchMethod(
                harmony,
                "TaleWorlds.MountAndBlade.MissionMultiplayerFlagDomination",
                "AfterStart",
                nameof(MissionMultiplayerFlagDomination_AfterStart_Prefix)) ? 1 : 0;
            patchedCount += TryPatchMethod(
                harmony,
                "TaleWorlds.MountAndBlade.MultiplayerTimerComponent",
                "StartTimerAsServer",
                nameof(MultiplayerTimerComponent_StartTimerAsServer_Prefix),
                typeof(float)) ? 1 : 0;
            patchedCount += TryPatchMethod(
                harmony,
                "TaleWorlds.MountAndBlade.MultiplayerTimerComponent",
                "StartTimerAsClient",
                nameof(MultiplayerTimerComponent_StartTimerAsClient_Prefix),
                typeof(float),
                typeof(float)) ? 1 : 0;
            patchedCount += TryPatchMethod(
                harmony,
                "TaleWorlds.MountAndBlade.Multiplayer.ConsoleMatchStartEndHandler",
                "OnMissionTick",
                nameof(ConsoleMatchStartEndHandler_OnMissionTick_Prefix),
                typeof(float)) ? 1 : 0;
            patchedCount += TryPatchMethod(
                harmony,
                "TaleWorlds.MountAndBlade.MultiplayerWarmupComponent",
                "AfterStart",
                nameof(MultiplayerWarmupComponent_AfterStart_Prefix)) ? 1 : 0;
            patchedCount += TryPatchMethod(
                harmony,
                "TaleWorlds.MountAndBlade.MultiplayerWarmupComponent",
                "OnPreDisplayMissionTick",
                nameof(MultiplayerWarmupComponent_OnPreDisplayMissionTick_Prefix),
                typeof(float)) ? 1 : 0;

            Type networkPeerType = AccessTools.TypeByName("TaleWorlds.MountAndBlade.NetworkCommunicator");
            if (networkPeerType != null)
            {
                patchedCount += TryPatchMethod(
                    harmony,
                    "TaleWorlds.MountAndBlade.MultiplayerWarmupComponent",
                    "HandleNewClientAfterSynchronized",
                    nameof(MultiplayerWarmupComponent_HandleNewClientAfterSynchronized_Prefix),
                    networkPeerType) ? 1 : 0;
            }
            else
            {
                ModLogger.Info("BattleShellSuppressionPatch: type not found. Type=TaleWorlds.MountAndBlade.NetworkCommunicator");
            }

            ModLogger.Info("BattleShellSuppressionPatch: native warmup/timer suppression patch pass completed. SuccessfulPatches=" + patchedCount + ".");
        }

        private static bool TryPatchMethod(Harmony harmony, string typeName, string methodName, string prefixMethodName, params Type[] parameterTypes)
        {
            try
            {
                return PatchMethod(harmony, typeName, methodName, prefixMethodName, parameterTypes);
            }
            catch (Exception ex)
            {
                ModLogger.Error("BattleShellSuppressionPatch: failed to patch " + typeName + "." + methodName + ".", ex);
                return false;
            }
        }

        private static bool PatchMethod(Harmony harmony, string typeName, string methodName, string prefixMethodName, params Type[] parameterTypes)
        {
            Type targetType = AccessTools.TypeByName(typeName);
            if (targetType == null)
            {
                ModLogger.Info("BattleShellSuppressionPatch: type not found. Type=" + typeName);
                return false;
            }

            BindingFlags flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            MethodInfo target = parameterTypes == null || parameterTypes.Length == 0
                ? targetType.GetMethod(methodName, flags)
                : targetType.GetMethod(methodName, flags, null, parameterTypes, null);
            MethodInfo prefix = typeof(BattleShellSuppressionPatch).GetMethod(prefixMethodName, BindingFlags.Static | BindingFlags.NonPublic);
            if (target == null || prefix == null)
            {
                ModLogger.Info("BattleShellSuppressionPatch: method not found. Type=" + typeName + " Method=" + methodName);
                return false;
            }

            harmony.Patch(target, prefix: new HarmonyMethod(prefix));
            ModLogger.Info("BattleShellSuppressionPatch: patched " + typeName + "." + methodName + ".");
            return true;
        }

        private static void MissionState_OnActivate_Prefix(object __instance)
        {
            LogMissionStateLifecycleObservation(
                __instance?.GetType().GetProperty("CurrentMission", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(__instance) as Mission,
                "MissionState.OnActivate");
        }

        private static bool MissionState_OnTick_Prefix(object __instance, float realDt)
        {
            if (TryHandleDedicatedEarlyMissionStateOnTick(__instance, realDt))
                return false;

            Mission mission = __instance?.GetType().GetProperty("CurrentMission", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(__instance) as Mission;
            if (mission == null)
                return true;

            Mission.State missionState = mission.CurrentState;
            if (missionState != Mission.State.NewlyCreated && missionState != Mission.State.Initializing)
                return true;

            LogMissionStateLifecycleObservation(mission, "MissionState.OnTick loading-step", "RealDt=" + realDt.ToString("0.0000"));
            return true;
        }

        private static bool MissionState_LoadMission_Prefix(object __instance)
        {
            if (TryHandleDedicatedEarlyLoadMissionWithoutPreload(__instance))
                return false;

            EnsureMissionScreenPreLoadBehaviorPatches(__instance);
            LogMissionBehaviorPreloadStack(__instance);
            LogMissionStateLoaderObservation(__instance, "MissionState.LoadMission");
            return true;
        }

        private static void MissionState_LoadMission_Postfix(object __instance)
        {
            LogMissionStateLoaderObservation(__instance, "MissionState.LoadMission completed");
        }

        private static void MissionState_TickLoading_Prefix(object __instance, float realDt)
        {
            LogMissionStateLoaderObservation(
                __instance,
                "MissionState.TickLoading",
                "RealDt=" + realDt.ToString("0.0000"));
        }

        private static void MissionState_TickLoading_Postfix(object __instance, float realDt)
        {
            LogMissionStateLoaderObservation(
                __instance,
                "MissionState.TickLoading completed",
                "RealDt=" + realDt.ToString("0.0000"));
        }

        private static void Mission_OnMissionStateActivate_Prefix(Mission __instance)
        {
            LogMissionStateLifecycleObservation(__instance, "Mission.OnMissionStateActivate");
        }

        private static void Mission_Initialize_Prefix(Mission __instance)
        {
            LogMissionStateLifecycleObservation(__instance, "Mission.Initialize");
        }

        private static void Mission_Initialize_Postfix(Mission __instance)
        {
            LogMissionStateLifecycleObservation(__instance, "Mission.Initialize completed");
        }

        private static void Mission_set_CurrentState_Prefix(Mission __instance, object value)
        {
            LogMissionCurrentStateSetObservation(__instance, value);
        }

        private static void Mission_get_IsLoadingFinished_Prefix(Mission __instance)
        {
            LogIsLoadingFinishedObservation(__instance, "Mission.get_IsLoadingFinished");
        }

        private static void Mission_get_IsLoadingFinished_Postfix(Mission __instance, bool __result)
        {
            LogIsLoadingFinishedObservation(__instance, "Mission.get_IsLoadingFinished completed", "Result=" + __result);
        }

        private static bool Mission_ClearUnreferencedResources_Prefix(Mission __instance, bool forceClearGPUResources)
        {
            if (ShouldSkipEarlyDedicatedMissionClearResources(__instance, forceClearGPUResources))
            {
                LogDedicatedMissionClearResourcesSkip(__instance, forceClearGPUResources);
                return false;
            }

            if (ShouldSuppressDedicatedClearResourcesObservation(__instance))
                return true;

            LogMissionStateLifecycleObservation(__instance, "Mission.ClearUnreferencedResources", "ForceClearGPUResources=" + forceClearGPUResources);
            return true;
        }

        private static bool MissionBehavior_OnMissionScreenPreLoad_Prefix(object __instance)
        {
            Mission mission = (__instance as MissionBehavior)?.Mission ?? Mission.Current;
            LogMissionBehaviorPreloadEntry(mission, __instance?.GetType());
            if (!ShouldSkipDedicatedMissionScreenPreLoad(mission))
                return true;

            LogDedicatedMissionScreenPreLoadSkip(mission, __instance?.GetType());
            return false;
        }

        private static void Mission_ClearUnreferencedResources_Postfix(Mission __instance, bool forceClearGPUResources)
        {
            if (ShouldSuppressDedicatedClearResourcesObservation(__instance))
                return;

            LogMissionStateLifecycleObservation(__instance, "Mission.ClearUnreferencedResources completed", "ForceClearGPUResources=" + forceClearGPUResources);
        }

        private static void Utilities_ClearOldResourcesAndObjects_Prefix()
        {
            LogEngineCleanupObservation("Utilities.ClearOldResourcesAndObjects");
        }

        private static void Utilities_ClearOldResourcesAndObjects_Postfix()
        {
            LogEngineCleanupObservation("Utilities.ClearOldResourcesAndObjects completed");
        }

        private static void Mission_AfterStart_Prefix(Mission __instance)
        {
            LogOfficialBattleStartupObservation(__instance, "Mission.AfterStart");
        }

        private static void MissionState_FinishMissionLoading_Prefix(object __instance)
        {
            try
            {
                Mission mission = __instance?.GetType().GetProperty("CurrentMission", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(__instance) as Mission;
                if (!GameNetwork.IsServer || mission == null)
                    return;

                string sceneName = mission.SceneName ?? string.Empty;
                if (!SceneRuntimeClassifier.IsSceneAwareBattleRuntimeScene(sceneName))
                    return;

                string key = sceneName + "|" + mission.Mode + "|" + mission.CurrentState;
                if (string.Equals(_lastFinishMissionLoadingObservationKey, key, StringComparison.Ordinal))
                    return;

                _lastFinishMissionLoadingObservationKey = key;
                ModLogger.Info(
                    "BattleShellSuppressionPatch: observed MissionState.FinishMissionLoading entry. " +
                    "Scene=" + sceneName +
                    " Mode=" + mission.Mode +
                    " MissionState=" + mission.CurrentState +
                    " HasLobbyComponent=" + (mission.GetMissionBehavior<MissionLobbyComponent>() != null) +
                    " HasMultiplayerRoundController=" + (mission.GetMissionBehavior<MultiplayerRoundController>() != null) +
                    " HasMissionMultiplayerFlagDomination=" + (mission.GetMissionBehavior<MissionMultiplayerFlagDomination>() != null) +
                    " HasMultiplayerWarmupComponent=" + (mission.GetMissionBehavior<MultiplayerWarmupComponent>() != null) + ".");
            }
            catch (Exception ex)
            {
                ModLogger.Info("BattleShellSuppressionPatch: MissionState.FinishMissionLoading observation failed: " + ex.Message);
            }
        }

        private static void Mission_Tick_Prefix(Mission __instance, float dt)
        {
            try
            {
                if (!GameNetwork.IsServer || __instance == null)
                    return;

                string sceneName = __instance.SceneName ?? string.Empty;
                if (!SceneRuntimeClassifier.IsSceneAwareBattleRuntimeScene(sceneName))
                    return;

                Mission.State missionState = __instance.CurrentState;
                if (missionState != Mission.State.NewlyCreated && missionState != Mission.State.Initializing)
                    return;

                if (dt > 0.0011f)
                    return;

                string key = sceneName + "|" + missionState + "|" + dt.ToString("0.0000");
                if (string.Equals(_lastMissionLoadingTickObservationKey, key, StringComparison.Ordinal))
                    return;

                _lastMissionLoadingTickObservationKey = key;
                ModLogger.Info(
                    "BattleShellSuppressionPatch: observed native Mission.Tick during mission-loading window. " +
                    "Scene=" + sceneName +
                    " Mode=" + __instance.Mode +
                    " MissionState=" + missionState +
                    " Dt=" + dt.ToString("0.0000") +
                    " HasLobbyComponent=" + (__instance.GetMissionBehavior<MissionLobbyComponent>() != null) +
                    " HasMultiplayerRoundController=" + (__instance.GetMissionBehavior<MultiplayerRoundController>() != null) +
                    " HasMissionMultiplayerFlagDomination=" + (__instance.GetMissionBehavior<MissionMultiplayerFlagDomination>() != null) +
                    " HasMultiplayerWarmupComponent=" + (__instance.GetMissionBehavior<MultiplayerWarmupComponent>() != null) + ".");
            }
            catch (Exception ex)
            {
                ModLogger.Info("BattleShellSuppressionPatch: Mission.Tick loading-window observation failed: " + ex.Message);
            }
        }

        private static void MissionLobbyComponent_AfterStart_Prefix(object __instance)
        {
            LogOfficialBattleStartupObservation((__instance as MissionBehavior)?.Mission ?? Mission.Current, "MissionLobbyComponent.AfterStart");
        }

        private static void MultiplayerRoundController_AfterStart_Prefix(object __instance)
        {
            LogOfficialBattleStartupObservation((__instance as MissionBehavior)?.Mission ?? Mission.Current, "MultiplayerRoundController.AfterStart");
        }

        private static void MissionMultiplayerFlagDomination_AfterStart_Prefix(object __instance)
        {
            LogOfficialBattleStartupObservation((__instance as MissionBehavior)?.Mission ?? Mission.Current, "MissionMultiplayerFlagDomination.AfterStart");
        }

        private static bool MultiplayerWarmupComponent_AfterStart_Prefix(object __instance)
        {
            LogWarmupAfterStartObservation(__instance);
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

            MissionLobbyComponent lobbyComponent = mission.GetMissionBehavior<MissionLobbyComponent>();
            MissionLobbyComponent.MultiplayerGameState? lobbyState = lobbyComponent?.CurrentMultiplayerState;
            CoopBattlePhase currentPhase = CoopBattlePhaseRuntimeState.GetPhase();
            if (lobbyState == MissionLobbyComponent.MultiplayerGameState.Ending ||
                currentPhase >= CoopBattlePhase.BattleEnded)
            {
                string passThroughKey =
                    (source ?? "unknown") + "|" +
                    (mission?.SceneName ?? "unknown") + "|" +
                    (lobbyState?.ToString() ?? "null") + "|" +
                    currentPhase;
                if (!string.Equals(_lastEndTransitionPassThroughLogKey, passThroughKey, StringComparison.Ordinal))
                {
                    _lastEndTransitionPassThroughLogKey = passThroughKey;
                    ModLogger.Info(
                        "BattleShellSuppressionPatch: allowed native battle shell path for end transition. " +
                        "Source=" + (source ?? "unknown") +
                        " Scene=" + (mission?.SceneName ?? "unknown") +
                        " LobbyState=" + (lobbyState?.ToString() ?? "null") +
                        " BattlePhase=" + currentPhase + ".");
                }

                return false;
            }

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

        private static void LogWarmupAfterStartObservation(object instance)
        {
            try
            {
                Mission mission = (instance as MissionBehavior)?.Mission ?? Mission.Current;
                if (mission == null)
                    return;

                string key =
                    (mission.SceneName ?? "null") + "|" +
                    mission.Mode + "|" +
                    GameNetwork.IsServer + "|" +
                    GameNetwork.IsClient;
                if (string.Equals(_lastWarmupAfterStartObservationKey, key, StringComparison.Ordinal))
                    return;

                _lastWarmupAfterStartObservationKey = key;
                ModLogger.Info(
                    "BattleShellSuppressionPatch: observed native MultiplayerWarmupComponent.AfterStart entry. " +
                    "Scene=" + (mission.SceneName ?? "unknown") +
                    " Mode=" + mission.Mode +
                    " IsServer=" + GameNetwork.IsServer +
                    " IsClient=" + GameNetwork.IsClient +
                    " HasLobbyComponent=" + (mission.GetMissionBehavior<MissionLobbyComponent>() != null) +
                    " HasTimerComponent=" + (mission.GetMissionBehavior<MultiplayerTimerComponent>() != null) +
                    " HasTeamSelectComponent=" + (mission.GetMissionBehavior<MultiplayerTeamSelectComponent>() != null) +
                    " HasCoopSpawnLogic=" + (mission.GetMissionBehavior<CoopMissionSpawnLogic>() != null) + ".");
            }
            catch (Exception ex)
            {
                ModLogger.Info("BattleShellSuppressionPatch: warmup AfterStart observation failed: " + ex.Message);
            }
        }

        private static void LogOfficialBattleStartupObservation(Mission mission, string source)
        {
            try
            {
                if (!GameNetwork.IsServer || mission == null)
                    return;

                string sceneName = mission.SceneName ?? string.Empty;
                if (!SceneRuntimeClassifier.IsSceneAwareBattleRuntimeScene(sceneName))
                    return;

                string key =
                    (source ?? "unknown") + "|" +
                    sceneName + "|" +
                    mission.Mode + "|" +
                    (mission.GetMissionBehavior<MissionLobbyComponent>() != null) + "|" +
                    (mission.GetMissionBehavior<MultiplayerRoundController>() != null) + "|" +
                    (mission.GetMissionBehavior<MissionMultiplayerFlagDomination>() != null) + "|" +
                    (mission.GetMissionBehavior<MultiplayerWarmupComponent>() != null);
                if (string.Equals(_lastOfficialBattleStartupObservationKey, key, StringComparison.Ordinal))
                    return;

                _lastOfficialBattleStartupObservationKey = key;
                ModLogger.Info(
                    "BattleShellSuppressionPatch: observed official battle startup step. " +
                    "Source=" + (source ?? "unknown") +
                    " Scene=" + sceneName +
                    " Mode=" + mission.Mode +
                    " HasMissionLobbyComponent=" + (mission.GetMissionBehavior<MissionLobbyComponent>() != null) +
                    " HasMultiplayerRoundController=" + (mission.GetMissionBehavior<MultiplayerRoundController>() != null) +
                    " HasMissionMultiplayerFlagDomination=" + (mission.GetMissionBehavior<MissionMultiplayerFlagDomination>() != null) +
                    " HasMultiplayerWarmupComponent=" + (mission.GetMissionBehavior<MultiplayerWarmupComponent>() != null) +
                    " HasMultiplayerTimerComponent=" + (mission.GetMissionBehavior<MultiplayerTimerComponent>() != null) + ".");
            }
            catch (Exception ex)
            {
                ModLogger.Info("BattleShellSuppressionPatch: official battle startup observation failed: " + ex.Message);
            }
        }

        private static void LogMissionStateLifecycleObservation(Mission mission, string source, string extra = null)
        {
            try
            {
                if (!GameNetwork.IsServer || mission == null)
                    return;

                string sceneName = mission.SceneName ?? string.Empty;
                if (!SceneRuntimeClassifier.IsSceneAwareBattleRuntimeScene(sceneName))
                    return;

                string key =
                    (source ?? "unknown") + "|" +
                    sceneName + "|" +
                    mission.Mode + "|" +
                    mission.CurrentState + "|" +
                    mission.IsLoadingFinished + "|" +
                    (mission.GetMissionBehavior<MissionLobbyComponent>() != null) + "|" +
                    (mission.GetMissionBehavior<MultiplayerRoundController>() != null) + "|" +
                    (mission.GetMissionBehavior<MissionMultiplayerFlagDomination>() != null) + "|" +
                    (mission.GetMissionBehavior<MultiplayerWarmupComponent>() != null);
                if (string.Equals(_lastEarlyMissionLoadingObservationKey, key, StringComparison.Ordinal))
                    return;

                _lastEarlyMissionLoadingObservationKey = key;
                ModLogger.Info(
                    "BattleShellSuppressionPatch: observed early mission-loading lifecycle step. " +
                    "Source=" + (source ?? "unknown") +
                    " Scene=" + sceneName +
                    " Mode=" + mission.Mode +
                    " MissionState=" + mission.CurrentState +
                    " IsLoadingFinished=" + mission.IsLoadingFinished +
                    " NeedsMemoryCleanup=" + mission.NeedsMemoryCleanup +
                    " HasMissionLobbyComponent=" + (mission.GetMissionBehavior<MissionLobbyComponent>() != null) +
                    " HasMultiplayerRoundController=" + (mission.GetMissionBehavior<MultiplayerRoundController>() != null) +
                    " HasMissionMultiplayerFlagDomination=" + (mission.GetMissionBehavior<MissionMultiplayerFlagDomination>() != null) +
                    " HasMultiplayerWarmupComponent=" + (mission.GetMissionBehavior<MultiplayerWarmupComponent>() != null) +
                    (string.IsNullOrWhiteSpace(extra) ? "." : " " + extra + "."));
            }
            catch (Exception ex)
            {
                ModLogger.Info("BattleShellSuppressionPatch: early mission-loading lifecycle observation failed: " + ex.Message);
            }
        }

        private static void LogEngineCleanupObservation(string source)
        {
            try
            {
                if (!GameNetwork.IsServer)
                    return;

                Mission mission = Mission.Current;
                if (mission == null)
                    return;

                string sceneName = mission.SceneName ?? string.Empty;
                if (!SceneRuntimeClassifier.IsSceneAwareBattleRuntimeScene(sceneName))
                    return;

                string key =
                    (source ?? "unknown") + "|" +
                    sceneName + "|" +
                    mission.Mode + "|" +
                    mission.CurrentState + "|" +
                    mission.IsLoadingFinished;
                if (string.Equals(_lastEngineCleanupObservationKey, key, StringComparison.Ordinal))
                    return;

                _lastEngineCleanupObservationKey = key;
                ModLogger.Info(
                    "BattleShellSuppressionPatch: observed engine cleanup boundary. " +
                    "Source=" + (source ?? "unknown") +
                    " Scene=" + sceneName +
                    " Mode=" + mission.Mode +
                    " MissionState=" + mission.CurrentState +
                    " IsLoadingFinished=" + mission.IsLoadingFinished + ".");
            }
            catch (Exception ex)
            {
                ModLogger.Info("BattleShellSuppressionPatch: engine cleanup observation failed: " + ex.Message);
            }
        }

        private static void LogTickLoadingObservation(Mission mission, string source, float realDt)
        {
            try
            {
                if (!GameNetwork.IsServer || mission == null)
                    return;

                string sceneName = mission.SceneName ?? string.Empty;
                if (!SceneRuntimeClassifier.IsSceneAwareBattleRuntimeScene(sceneName))
                    return;

                string key =
                    (source ?? "unknown") + "|" +
                    sceneName + "|" +
                    mission.CurrentState + "|" +
                    mission.IsLoadingFinished + "|" +
                    realDt.ToString("0.0000");
                if (string.Equals(_lastTickLoadingObservationKey, key, StringComparison.Ordinal))
                    return;

                _lastTickLoadingObservationKey = key;
                ModLogger.Info(
                    "BattleShellSuppressionPatch: observed TickLoading boundary. " +
                    "Source=" + (source ?? "unknown") +
                    " Scene=" + sceneName +
                    " Mode=" + mission.Mode +
                    " MissionState=" + mission.CurrentState +
                    " IsLoadingFinished=" + mission.IsLoadingFinished +
                    " RealDt=" + realDt.ToString("0.0000") + ".");
            }
            catch (Exception ex)
            {
                ModLogger.Info("BattleShellSuppressionPatch: TickLoading observation failed: " + ex.Message);
            }
        }

        private static void LogMissionStateLoaderObservation(object missionStateInstance, string source, string extra = null)
        {
            try
            {
                if (!GameNetwork.IsServer || missionStateInstance == null)
                    return;

                Type missionStateType = missionStateInstance.GetType();
                Mission mission = missionStateType
                    .GetProperty("CurrentMission", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    ?.GetValue(missionStateInstance) as Mission;
                if (mission == null)
                    return;

                string sceneName = mission.SceneName ?? string.Empty;
                if (!SceneRuntimeClassifier.IsSceneAwareBattleRuntimeScene(sceneName))
                    return;

                object missionInitializingRaw = missionStateType
                    .GetField("_missionInitializing", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.GetValue(missionStateInstance);
                object tickCountRaw = missionStateType
                    .GetField("_tickCountBeforeLoad", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.GetValue(missionStateInstance);

                string key =
                    (source ?? "unknown") + "|" +
                    sceneName + "|" +
                    mission.Mode + "|" +
                    mission.CurrentState + "|" +
                    mission.IsLoadingFinished + "|" +
                    (missionInitializingRaw?.ToString() ?? "null") + "|" +
                    (tickCountRaw?.ToString() ?? "null") + "|" +
                    (extra ?? string.Empty);
                if (string.Equals(_lastMissionStateLoaderObservationKey, key, StringComparison.Ordinal))
                    return;

                _lastMissionStateLoaderObservationKey = key;
                ModLogger.Info(
                    "BattleShellSuppressionPatch: observed mission-state loader boundary. " +
                    "Source=" + (source ?? "unknown") +
                    " Scene=" + sceneName +
                    " Mode=" + mission.Mode +
                    " MissionState=" + mission.CurrentState +
                    " IsLoadingFinished=" + mission.IsLoadingFinished +
                    " MissionInitializing=" + (missionInitializingRaw?.ToString() ?? "null") +
                    " TickCountBeforeLoad=" + (tickCountRaw?.ToString() ?? "null") +
                    (string.IsNullOrWhiteSpace(extra) ? "." : " " + extra + "."));
            }
            catch (Exception ex)
            {
                ModLogger.Info("BattleShellSuppressionPatch: mission-state loader observation failed: " + ex.Message);
            }
        }

        private static void LogMissionCurrentStateSetObservation(Mission mission, object value)
        {
            try
            {
                if (!GameNetwork.IsServer || mission == null)
                    return;

                string sceneName = mission.SceneName ?? string.Empty;
                if (!SceneRuntimeClassifier.IsSceneAwareBattleRuntimeScene(sceneName))
                    return;

                string key =
                    sceneName + "|" +
                    mission.Mode + "|" +
                    mission.CurrentState + "->" +
                    (value?.ToString() ?? "null");
                if (string.Equals(_lastMissionCurrentStateSetObservationKey, key, StringComparison.Ordinal))
                    return;

                _lastMissionCurrentStateSetObservationKey = key;
                ModLogger.Info(
                    "BattleShellSuppressionPatch: observed Mission.CurrentState transition request. " +
                    "Scene=" + sceneName +
                    " Mode=" + mission.Mode +
                    " PreviousState=" + mission.CurrentState +
                    " NextState=" + (value?.ToString() ?? "null") + ".");
            }
            catch (Exception ex)
            {
                ModLogger.Info("BattleShellSuppressionPatch: Mission.CurrentState transition observation failed: " + ex.Message);
            }
        }

        private static bool ShouldSkipEarlyDedicatedMissionClearResources(Mission mission, bool forceClearGPUResources)
        {
            if (!GameNetwork.IsServer || mission == null || !forceClearGPUResources)
                return false;

            if (!IsDedicatedServerProcess())
                return false;

            if (!TryGetMissionState(mission, out Mission.State missionState) ||
                missionState != Mission.State.NewlyCreated)
            {
                return false;
            }

            string sceneName = mission.SceneName ?? string.Empty;
            if (!SceneRuntimeClassifier.IsSceneAwareBattleRuntimeScene(sceneName))
                return false;

            return !mission.IsLoadingFinished;
        }

        private static bool ShouldSuppressDedicatedClearResourcesObservation(Mission mission)
        {
            if (!GameNetwork.IsServer || mission == null)
                return false;

            if (!IsDedicatedServerProcess())
                return false;

            if (!TryGetMissionState(mission, out Mission.State missionState))
                return true;

            return missionState != Mission.State.NewlyCreated &&
                   missionState != Mission.State.Initializing;
        }

        private static bool ShouldSkipDedicatedMissionScreenPreLoad(Mission mission)
        {
            if (!GameNetwork.IsServer || mission == null)
                return false;

            if (!IsDedicatedServerProcess())
                return false;

            string sceneName = mission.SceneName ?? string.Empty;
            if (!SceneRuntimeClassifier.IsSceneAwareBattleRuntimeScene(sceneName))
                return false;

            return mission.CurrentState == Mission.State.NewlyCreated;
        }

        private static void LogDedicatedMissionClearResourcesSkip(Mission mission, bool forceClearGPUResources)
        {
            try
            {
                if (mission == null)
                    return;

                string sceneName = mission.SceneName ?? string.Empty;
                string pointer = TryGetMissionPointerHex(mission);
                string key =
                    sceneName + "|" +
                    mission.Mode + "|" +
                    mission.CurrentState + "|" +
                    mission.IsLoadingFinished + "|" +
                    forceClearGPUResources + "|" +
                    pointer;
                if (string.Equals(_lastClearUnreferencedResourcesSkipObservationKey, key, StringComparison.Ordinal))
                    return;

                _lastClearUnreferencedResourcesSkipObservationKey = key;
                ModLogger.Info(
                    "BattleShellSuppressionPatch: skipped early dedicated Mission.ClearUnreferencedResources to avoid native startup crash. " +
                    "Scene=" + sceneName +
                    " Mode=" + mission.Mode +
                    " MissionState=" + mission.CurrentState +
                    " IsLoadingFinished=" + mission.IsLoadingFinished +
                    " NeedsMemoryCleanup=" + mission.NeedsMemoryCleanup +
                    " ForceClearGPUResources=" + forceClearGPUResources +
                    " MissionPointer=" + pointer + ".");
            }
            catch (Exception ex)
            {
                ModLogger.Info("BattleShellSuppressionPatch: dedicated ClearUnreferencedResources skip observation failed: " + ex.Message);
            }
        }

        private static void LogDedicatedMissionScreenPreLoadSkip(Mission mission, Type behaviorType)
        {
            try
            {
                if (mission == null)
                    return;

                string sceneName = mission.SceneName ?? string.Empty;
                string behaviorTypeName = behaviorType?.FullName ?? "unknown";
                string key =
                    sceneName + "|" +
                    mission.Mode + "|" +
                    mission.CurrentState + "|" +
                    behaviorTypeName;
                if (string.Equals(_lastMissionScreenPreLoadSkipObservationKey, key, StringComparison.Ordinal))
                    return;

                _lastMissionScreenPreLoadSkipObservationKey = key;
                ModLogger.Info(
                    "BattleShellSuppressionPatch: skipped dedicated MissionBehavior.OnMissionScreenPreLoad during early battle startup. " +
                    "Scene=" + sceneName +
                    " Mode=" + mission.Mode +
                    " MissionState=" + mission.CurrentState +
                    " BehaviorType=" + behaviorTypeName + ".");
            }
            catch (Exception ex)
            {
                ModLogger.Info("BattleShellSuppressionPatch: dedicated MissionBehavior.OnMissionScreenPreLoad skip observation failed: " + ex.Message);
            }
        }

        private static void LogDedicatedMissionScreenPreLoadLoopSkip(Mission mission, List<MissionBehavior> missionBehaviors)
        {
            try
            {
                if (mission == null)
                    return;

                string sceneName = mission.SceneName ?? string.Empty;
                string key =
                    sceneName + "|" +
                    mission.Mode + "|" +
                    mission.CurrentState + "|" +
                    (missionBehaviors?.Count.ToString() ?? "null");
                if (string.Equals(_lastMissionScreenPreLoadLoopSkipObservationKey, key, StringComparison.Ordinal))
                    return;

                _lastMissionScreenPreLoadLoopSkipObservationKey = key;
                ModLogger.Info(
                    "BattleShellSuppressionPatch: skipped dedicated MissionBehavior.OnMissionScreenPreLoad loop during early battle startup. " +
                    "Scene=" + sceneName +
                    " Mode=" + mission.Mode +
                    " MissionState=" + mission.CurrentState +
                    " Count=" + (missionBehaviors?.Count.ToString() ?? "null") + ".");
            }
            catch (Exception ex)
            {
                ModLogger.Info("BattleShellSuppressionPatch: dedicated MissionBehavior.OnMissionScreenPreLoad loop skip observation failed: " + ex.Message);
            }
        }

        private static void LogMissionBehaviorPreloadEntry(Mission mission, Type behaviorType)
        {
            try
            {
                if (!GameNetwork.IsServer || mission == null)
                    return;

                string sceneName = mission.SceneName ?? string.Empty;
                if (!SceneRuntimeClassifier.IsSceneAwareBattleRuntimeScene(sceneName))
                    return;

                string behaviorTypeName = behaviorType?.FullName ?? "unknown";
                string key =
                    sceneName + "|" +
                    mission.Mode + "|" +
                    mission.CurrentState + "|" +
                    behaviorTypeName;
                if (string.Equals(_lastMissionScreenPreLoadEntryObservationKey, key, StringComparison.Ordinal))
                    return;

                _lastMissionScreenPreLoadEntryObservationKey = key;
                ModLogger.Info(
                    "BattleShellSuppressionPatch: entering MissionBehavior.OnMissionScreenPreLoad. " +
                    "Scene=" + sceneName +
                    " Mode=" + mission.Mode +
                    " MissionState=" + mission.CurrentState +
                    " BehaviorType=" + behaviorTypeName + ".");
            }
            catch (Exception ex)
            {
                ModLogger.Info("BattleShellSuppressionPatch: MissionBehavior.OnMissionScreenPreLoad entry observation failed: " + ex.Message);
            }
        }

        private static void LogMissionBehaviorPreloadStack(object missionStateInstance)
        {
            try
            {
                if (!GameNetwork.IsServer || missionStateInstance == null)
                    return;

                Mission mission = missionStateInstance.GetType()
                    .GetProperty("CurrentMission", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    ?.GetValue(missionStateInstance) as Mission;
                if (mission == null)
                    return;

                string sceneName = mission.SceneName ?? string.Empty;
                if (!SceneRuntimeClassifier.IsSceneAwareBattleRuntimeScene(sceneName))
                    return;

                List<MissionBehavior> missionBehaviors = mission.MissionBehaviors;
                if (missionBehaviors == null)
                    return;

                List<string> behaviorTypes = new List<string>(missionBehaviors.Count);
                for (int i = 0; i < missionBehaviors.Count; i++)
                {
                    behaviorTypes.Add((missionBehaviors[i]?.GetType().FullName ?? "null") + "#" + i);
                }

                string joined = string.Join(", ", behaviorTypes.ToArray());
                string key =
                    sceneName + "|" +
                    mission.Mode + "|" +
                    mission.CurrentState + "|" +
                    joined;
                if (string.Equals(_lastMissionBehaviorStackObservationKey, key, StringComparison.Ordinal))
                    return;

                _lastMissionBehaviorStackObservationKey = key;
                ModLogger.Info(
                    "BattleShellSuppressionPatch: observed mission behavior preload stack. " +
                    "Scene=" + sceneName +
                    " Mode=" + mission.Mode +
                    " MissionState=" + mission.CurrentState +
                    " Count=" + missionBehaviors.Count +
                    " Behaviors=[" + joined + "].");
            }
            catch (Exception ex)
            {
                ModLogger.Info("BattleShellSuppressionPatch: mission behavior preload stack observation failed: " + ex.Message);
            }
        }

        private static bool TryHandleDedicatedEarlyLoadMissionWithoutPreload(object missionStateInstance)
        {
            try
            {
                if (!GameNetwork.IsServer || missionStateInstance == null || !IsDedicatedServerProcess())
                    return false;

                Type missionStateType = missionStateInstance.GetType();
                Mission mission = missionStateType
                    .GetProperty("CurrentMission", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    ?.GetValue(missionStateInstance) as Mission;
                if (!ShouldSkipDedicatedMissionScreenPreLoad(mission))
                    return false;

                FieldInfo missionInitializingField = missionStateType.GetField("_missionInitializing", BindingFlags.Instance | BindingFlags.NonPublic);
                if (missionInitializingField == null)
                {
                    ModLogger.Info("BattleShellSuppressionPatch: cannot bypass dedicated MissionState.LoadMission preload loop because _missionInitializing field was not found.");
                    return false;
                }

                LogDedicatedManualLoadMissionStep(mission, "entered");
                LogDedicatedManualLoadMissionStep(mission, "skipped engine cleanup");
                LogDedicatedManualLoadMissionStep(mission, "before mission-initializing flag");
                missionInitializingField.SetValue(missionStateInstance, true);
                LogDedicatedManualLoadMissionStep(mission, "before Mission.Initialize");
                mission.Initialize();
                LogDedicatedManualLoadMissionStep(mission, "completed");
                return true;
            }
            catch (Exception ex)
            {
                ModLogger.Error("BattleShellSuppressionPatch: dedicated manual MissionState.LoadMission preload bypass failed; falling back to original LoadMission.", ex);
                return false;
            }
        }

        private static bool TryHandleDedicatedEarlyMissionStateOnTick(object missionStateInstance, float realDt)
        {
            try
            {
                if (!GameNetwork.IsServer || missionStateInstance == null || !IsDedicatedServerProcess())
                    return false;

                Type missionStateType = missionStateInstance.GetType();
                Mission mission = missionStateType
                    .GetProperty("CurrentMission", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    ?.GetValue(missionStateInstance) as Mission;
                if (mission == null)
                    return false;

                Mission.State missionState = mission.CurrentState;
                if (missionState != Mission.State.NewlyCreated && missionState != Mission.State.Initializing)
                    return false;

                string sceneName = mission.SceneName ?? string.Empty;
                if (!SceneRuntimeClassifier.IsSceneAwareBattleRuntimeScene(sceneName))
                    return false;

                if (missionState == Mission.State.NewlyCreated)
                {
                    bool handledLoadMission = TryHandleDedicatedEarlyLoadMissionWithoutPreload(missionStateInstance);
                    if (!handledLoadMission)
                        ModLogger.Info("BattleShellSuppressionPatch: dedicated MissionState.OnTick skipped original loading branch but manual LoadMission was unavailable.");
                    return true;
                }

                if (missionState == Mission.State.Initializing)
                {
                    LogDedicatedManualOnTickStep(mission, "skipped base OnTick", realDt);
                    MethodInfo finishMissionLoadingMethod = missionStateType.GetMethod("FinishMissionLoading", BindingFlags.Instance | BindingFlags.NonPublic);
                    if (finishMissionLoadingMethod == null)
                    {
                        ModLogger.Info("BattleShellSuppressionPatch: cannot finish dedicated manual MissionState.OnTick loading branch because FinishMissionLoading method was not found.");
                        return true;
                    }

                    LogDedicatedManualOnTickStep(mission, "before IsLoadingFinished check", realDt);
                    bool isLoadingFinished = mission.IsLoadingFinished;
                    LogDedicatedManualOnTickStep(mission, isLoadingFinished ? "IsLoadingFinished true" : "IsLoadingFinished false", realDt);
                    if (isLoadingFinished)
                    {
                        LogDedicatedManualOnTickStep(mission, "before FinishMissionLoading", realDt);
                        finishMissionLoadingMethod.Invoke(missionStateInstance, Array.Empty<object>());
                        LogDedicatedManualOnTickStep(mission, "after FinishMissionLoading", realDt);
                    }
                }

                return true;
            }
            catch (TargetInvocationException ex)
            {
                ModLogger.Error("BattleShellSuppressionPatch: dedicated manual MissionState.OnTick loading bypass failed; falling back to original OnTick.", ex.InnerException ?? ex);
                return false;
            }
            catch (Exception ex)
            {
                ModLogger.Error("BattleShellSuppressionPatch: dedicated manual MissionState.OnTick loading bypass failed; falling back to original OnTick.", ex);
                return false;
            }
        }

        private static void LogDedicatedManualLoadMissionStep(Mission mission, string step)
        {
            try
            {
                if (mission == null)
                    return;

                string sceneName = mission.SceneName ?? string.Empty;
                string key =
                    (step ?? "unknown") + "|" +
                    sceneName + "|" +
                    mission.Mode + "|" +
                    mission.CurrentState;
                if (string.Equals(_lastDedicatedManualLoadMissionStepKey, key, StringComparison.Ordinal))
                    return;

                _lastDedicatedManualLoadMissionStepKey = key;
                ModLogger.Info(
                    "BattleShellSuppressionPatch: dedicated manual MissionState.LoadMission preload bypass step. " +
                    "Step=" + (step ?? "unknown") +
                    " Scene=" + sceneName +
                    " Mode=" + mission.Mode +
                    " MissionState=" + mission.CurrentState + ".");
            }
            catch (Exception ex)
            {
                ModLogger.Info("BattleShellSuppressionPatch: dedicated manual LoadMission step observation failed: " + ex.Message);
            }
        }

        private static void LogDedicatedManualOnTickStep(Mission mission, string step, float realDt)
        {
            try
            {
                if (mission == null)
                    return;

                string sceneName = mission.SceneName ?? string.Empty;
                string key =
                    (step ?? "unknown") + "|" +
                    sceneName + "|" +
                    mission.Mode + "|" +
                    mission.CurrentState + "|" +
                    realDt.ToString("0.0000");
                if (string.Equals(_lastDedicatedManualOnTickStepKey, key, StringComparison.Ordinal))
                    return;

                _lastDedicatedManualOnTickStepKey = key;
                ModLogger.Info(
                    "BattleShellSuppressionPatch: dedicated manual MissionState.OnTick loading bypass step. " +
                    "Step=" + (step ?? "unknown") +
                    " Scene=" + sceneName +
                    " Mode=" + mission.Mode +
                    " MissionState=" + mission.CurrentState +
                    " RealDt=" + realDt.ToString("0.0000") + ".");
            }
            catch (Exception ex)
            {
                ModLogger.Info("BattleShellSuppressionPatch: dedicated manual OnTick step observation failed: " + ex.Message);
            }
        }

        private static void EnsureMissionScreenPreLoadBehaviorPatches(object missionStateInstance)
        {
            try
            {
                if (_runtimeHarmony == null || !GameNetwork.IsServer || !IsDedicatedServerProcess())
                    return;

                Mission mission = missionStateInstance?.GetType().GetProperty("CurrentMission", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(missionStateInstance) as Mission;
                if (!ShouldSkipDedicatedMissionScreenPreLoad(mission))
                    return;

                List<MissionBehavior> missionBehaviors = mission?.MissionBehaviors;
                if (missionBehaviors == null || missionBehaviors.Count == 0)
                    return;

                MethodInfo prefix = typeof(BattleShellSuppressionPatch).GetMethod(nameof(MissionBehavior_OnMissionScreenPreLoad_Prefix), BindingFlags.Static | BindingFlags.NonPublic);
                if (prefix == null)
                    return;

                for (int i = 0; i < missionBehaviors.Count; i++)
                {
                    MissionBehavior behavior = missionBehaviors[i];
                    if (behavior == null)
                        continue;

                    MethodInfo target = behavior.GetType().GetMethod(
                        "OnMissionScreenPreLoad",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                    if (target == null)
                        continue;

                    string patchKey =
                        (target.DeclaringType?.FullName ?? "unknown") + "::" +
                        target.Name + "::" +
                        target.MetadataToken;
                    if (string.IsNullOrWhiteSpace(patchKey) || _patchedMissionScreenPreLoadMethods.Contains(patchKey))
                        continue;

                    _runtimeHarmony.Patch(target, prefix: new HarmonyMethod(prefix));
                    _patchedMissionScreenPreLoadMethods.Add(patchKey);
                    ModLogger.Info("BattleShellSuppressionPatch: patched mission behavior preload hook. BehaviorType=" + (behavior.GetType().FullName ?? "unknown") + " TargetDeclaringType=" + (target.DeclaringType?.FullName ?? "unknown") + ".");
                }
            }
            catch (Exception ex)
            {
                ModLogger.Info("BattleShellSuppressionPatch: failed to patch mission behavior preload hooks: " + ex.Message);
            }
        }

        private static string FormatPointer(UIntPtr pointer)
        {
            try
            {
                return "0x" + pointer.ToUInt64().ToString("X");
            }
            catch
            {
                return pointer.ToString();
            }
        }

        private static string TryGetMissionPointerHex(Mission mission)
        {
            try
            {
                object value = typeof(Mission).GetProperty("Pointer", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(mission, null);
                if (value is UIntPtr pointer)
                    return FormatPointer(pointer);
            }
            catch
            {
            }

            return "unavailable";
        }

        private static bool IsDedicatedServerProcess()
        {
            try
            {
                string processPath = Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
                if (processPath.IndexOf("Win64_Shipping_Server", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;

                string processName = Process.GetCurrentProcess().ProcessName ?? string.Empty;
                return processName.IndexOf("Win64_Shipping_Server", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch
            {
                return false;
            }
        }

        private static void LogIsLoadingFinishedObservation(Mission mission, string source, string extra = null)
        {
            try
            {
                if (!GameNetwork.IsServer || mission == null)
                    return;

                if (IsDedicatedServerProcess() &&
                    TryGetMissionState(mission, out Mission.State missionState) &&
                    missionState != Mission.State.NewlyCreated &&
                    missionState != Mission.State.Initializing)
                {
                    return;
                }

                string sceneName = mission.SceneName ?? string.Empty;
                if (!SceneRuntimeClassifier.IsSceneAwareBattleRuntimeScene(sceneName))
                    return;

                string key =
                    (source ?? "unknown") + "|" +
                    sceneName + "|" +
                    mission.CurrentState + "|" +
                    (extra ?? string.Empty);
                if (string.Equals(_lastIsLoadingFinishedObservationKey, key, StringComparison.Ordinal))
                    return;

                _lastIsLoadingFinishedObservationKey = key;
                ModLogger.Info(
                    "BattleShellSuppressionPatch: observed IsLoadingFinished boundary. " +
                    "Source=" + (source ?? "unknown") +
                    " Scene=" + sceneName +
                    " Mode=" + mission.Mode +
                    " MissionState=" + mission.CurrentState +
                    (string.IsNullOrWhiteSpace(extra) ? "." : " " + extra + "."));
            }
            catch (Exception ex)
            {
                ModLogger.Info("BattleShellSuppressionPatch: IsLoadingFinished observation failed: " + ex.Message);
            }
        }

        private static bool TryGetMissionState(Mission mission, out Mission.State missionState)
        {
            missionState = default;
            if (mission == null)
                return false;

            try
            {
                missionState = mission.CurrentState;
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
