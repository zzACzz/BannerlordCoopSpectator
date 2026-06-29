using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Reflection;
using CoopSpectator.GameMode;
using CoopSpectator.Infrastructure;
using CoopSpectator.MissionBehaviors;
using CoopSpectator.Network.Messages;
using HarmonyLib;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.Missions.Multiplayer;
using TaleWorlds.ObjectSystem;

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
        private static string _lastClientBootstrapPassThroughLogKey;
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
        private static string _lastDedicatedSiegeWarmupAfterStartSuppressionKey;
        private static string _lastDedicatedSiegeWarmupPreDisplaySuppressionKey;
        private static string _lastDedicatedSiegeWarmupSpawningTickSuppressionKey;
        private static string _lastDedicatedSiegeTimerStartSuppressionKey;
        private static string _lastDedicatedSiegeTeamTickSuppressionKey;
        private static string _lastAfterStartPostfixObservationKey;
        private static string _lastSiegeStartupPassThroughLogKey;
        private static string _lastSiegeLobbyEarlyStartDiagnosticsKey;
        private static string _lastSiegeTeamAddDiagnosticsKey;
        private static string _lastSiegePlayerTeamBootstrapKey;
        private static string _lastMissionPreloadViewSkipKey;
        private static string _lastMissionPreloadViewRemovalKey;
        private static string _lastRemoteClientSiegeCampaignOnlyViewRemovalKey;
        private static string _lastMusicBattleMissionViewAfterStartSuppressionKey;
        private static string _lastMusicBattleMissionViewFinalizeSuppressionKey;
        private static bool _missionPreloadViewPatchApplied;
        private static readonly HashSet<string> RemoteClientSiegeCampaignOnlyMissionViewTypeNames =
            new HashSet<string>(StringComparer.Ordinal)
            {
                "TaleWorlds.MountAndBlade.GauntletUI.Mission.Singleplayer.MissionGauntletBattleScore",
                "MissionGauntletBattleScore",
                "TaleWorlds.MountAndBlade.GauntletUI.Mission.Singleplayer.MissionGauntletSingleplayerOrderUIHandler",
                "MissionGauntletSingleplayerOrderUIHandler",
                "TaleWorlds.MountAndBlade.GauntletUI.Mission.Singleplayer.MissionGauntletOrderOfBattleUIHandler",
                "MissionGauntletOrderOfBattleUIHandler",
                "TaleWorlds.MountAndBlade.GauntletUI.Mission.Singleplayer.MissionGauntletSiegeEngineMarker",
                "MissionGauntletSiegeEngineMarker",
                "TaleWorlds.MountAndBlade.View.MissionViews.Singleplayer.DeploymentMissionView",
                "DeploymentMissionView",
                "SandBox.View.Missions.MissionPreloadView",
                "MissionPreloadView",
                "SandBox.View.Missions.MissionCampaignBattleSpectatorView",
                "MissionCampaignBattleSpectatorView",
                "TaleWorlds.MountAndBlade.View.MissionViews.Singleplayer.MissionEntitySelectionUIHandler",
                "MissionEntitySelectionUIHandler",
                "TaleWorlds.MountAndBlade.GauntletUI.Mission.Singleplayer.MissionGauntletSpectatorControl",
                "MissionGauntletSpectatorControl",
                "SandBox.View.Missions.MissionSingleplayerViewHandler",
                "MissionSingleplayerViewHandler",
                "TaleWorlds.MountAndBlade.View.MissionViews.Singleplayer.MissionSingleplayerEscapeMenu",
                "MissionSingleplayerEscapeMenu",
                "TaleWorlds.MountAndBlade.GauntletUI.Mission.Singleplayer.MissionGauntletSingleplayerEscapeMenu",
                "MissionGauntletSingleplayerEscapeMenu",
                "TaleWorlds.MountAndBlade.View.MissionViews.Singleplayer.MissionLeaveView",
                "MissionLeaveView",
                "TaleWorlds.MountAndBlade.GauntletUI.Mission.Singleplayer.MissionGauntletLeaveView",
                "MissionGauntletLeaveView"
            };
        private static readonly HashSet<string> _patchedMissionScreenPreLoadMethods = new HashSet<string>(StringComparer.Ordinal);
        private static Harmony _runtimeHarmony;
        private const bool EnableDedicatedMissionLoadBypass = false;

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
            patchedCount += TryPatchPostfixMethod(
                harmony,
                "TaleWorlds.MountAndBlade.Mission",
                "AfterStart",
                nameof(Mission_AfterStart_Postfix)) ? 1 : 0;
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
                "TaleWorlds.MountAndBlade.Mission",
                "OnPreTick",
                nameof(Mission_OnPreTick_Prefix),
                typeof(float)) ? 1 : 0;
            patchedCount += TryPatchMethod(
                harmony,
                "TaleWorlds.MountAndBlade.Team",
                "Tick",
                nameof(Team_Tick_Prefix),
                typeof(float)) ? 1 : 0;
            patchedCount += TryPatchMethod(
                harmony,
                "TaleWorlds.MountAndBlade.MissionLobbyComponent",
                "EarlyStart",
                nameof(MissionLobbyComponent_EarlyStart_Prefix)) ? 1 : 0;
            patchedCount += TryPatchMethod(
                harmony,
                "TaleWorlds.MountAndBlade.MissionLobbyComponent",
                "AfterStart",
                nameof(MissionLobbyComponent_AfterStart_Prefix)) ? 1 : 0;
            patchedCount += TryPatchPostfixMethod(
                harmony,
                "TaleWorlds.MountAndBlade.MissionLobbyComponent",
                "AfterStart",
                nameof(MissionLobbyComponent_AfterStart_Postfix)) ? 1 : 0;
            patchedCount += TryPatchMethod(
                harmony,
                "TaleWorlds.MountAndBlade.DedicatedCustomServer.MissionCustomGameServerComponent",
                "AfterStart",
                nameof(MissionCustomGameServerComponent_AfterStart_Prefix)) ? 1 : 0;
            patchedCount += TryPatchPostfixMethod(
                harmony,
                "TaleWorlds.MountAndBlade.DedicatedCustomServer.MissionCustomGameServerComponent",
                "AfterStart",
                nameof(MissionCustomGameServerComponent_AfterStart_Postfix)) ? 1 : 0;
            patchedCount += TryPatchMethod(
                harmony,
                "TaleWorlds.MountAndBlade.MissionScoreboardComponent",
                "AfterStart",
                nameof(MissionScoreboardComponent_AfterStart_Prefix)) ? 1 : 0;
            patchedCount += TryPatchPostfixMethod(
                harmony,
                "TaleWorlds.MountAndBlade.MissionScoreboardComponent",
                "AfterStart",
                nameof(MissionScoreboardComponent_AfterStart_Postfix)) ? 1 : 0;
            patchedCount += TryPatchMethod(
                harmony,
                "TaleWorlds.MountAndBlade.MultiplayerTeamSelectComponent",
                "AfterStart",
                nameof(MultiplayerTeamSelectComponent_AfterStart_Prefix)) ? 1 : 0;
            patchedCount += TryPatchPostfixMethod(
                harmony,
                "TaleWorlds.MountAndBlade.MultiplayerTeamSelectComponent",
                "AfterStart",
                nameof(MultiplayerTeamSelectComponent_AfterStart_Postfix)) ? 1 : 0;
            patchedCount += TryPatchMethod(
                harmony,
                "TaleWorlds.MountAndBlade.Mission+TeamCollection",
                "Add",
                nameof(MissionTeamCollection_Add_Prefix),
                typeof(BattleSideEnum),
                typeof(uint),
                typeof(uint),
                typeof(Banner),
                typeof(bool),
                typeof(bool),
                typeof(bool)) ? 1 : 0;
            patchedCount += TryEnsureMissionPreloadViewPatch("Apply") ? 1 : 0;
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
            patchedCount += TryPatchMethod(
                harmony,
                "TaleWorlds.MountAndBlade.WarmupSpawningBehavior",
                "OnTick",
                nameof(WarmupSpawningBehavior_OnTick_Prefix),
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

        public static void ApplyClientMissionLoadingDiagnosticsOnly(Harmony harmony)
        {
            _runtimeHarmony = harmony;
            int patchedCount = 0;
            patchedCount += TryPatchMethod(
                harmony,
                "TaleWorlds.MountAndBlade.MissionState",
                "LoadMission",
                nameof(MissionState_LoadMission_DiagnosticsOnly_Prefix)) ? 1 : 0;
            patchedCount += TryPatchPostfixMethod(
                harmony,
                "TaleWorlds.MountAndBlade.MissionState",
                "LoadMission",
                nameof(MissionState_LoadMission_DiagnosticsOnly_Postfix)) ? 1 : 0;
            patchedCount += TryPatchMethod(
                harmony,
                "TaleWorlds.MountAndBlade.MissionState",
                "TickLoading",
                nameof(MissionState_TickLoading_Prefix),
                typeof(float)) ? 1 : 0;
            patchedCount += TryPatchPostfixMethod(
                harmony,
                "TaleWorlds.MountAndBlade.MissionState",
                "TickLoading",
                nameof(MissionState_TickLoading_Postfix),
                typeof(float)) ? 1 : 0;
            patchedCount += TryPatchMethod(
                harmony,
                "TaleWorlds.MountAndBlade.Mission",
                "Initialize",
                nameof(Mission_Initialize_Prefix)) ? 1 : 0;
            patchedCount += TryPatchPostfixMethod(
                harmony,
                "TaleWorlds.MountAndBlade.Mission",
                "Initialize",
                nameof(Mission_Initialize_Postfix)) ? 1 : 0;
            patchedCount += TryPatchMethod(
                harmony,
                "TaleWorlds.MountAndBlade.Mission",
                "get_IsLoadingFinished",
                nameof(Mission_get_IsLoadingFinished_Prefix)) ? 1 : 0;
            patchedCount += TryPatchPostfixMethod(
                harmony,
                "TaleWorlds.MountAndBlade.Mission",
                "get_IsLoadingFinished",
                nameof(Mission_get_IsLoadingFinished_Postfix)) ? 1 : 0;
            patchedCount += TryPatchMethod(
                harmony,
                "TaleWorlds.MountAndBlade.MissionState",
                "FinishMissionLoading",
                nameof(MissionState_FinishMissionLoading_DiagnosticsOnly_Prefix)) ? 1 : 0;
            patchedCount += TryPatchMethod(
                harmony,
                "TaleWorlds.MountAndBlade.MissionBehavior",
                "OnMissionScreenPreLoad",
                nameof(MissionBehavior_OnMissionScreenPreLoad_DiagnosticsOnly_Prefix)) ? 1 : 0;
            patchedCount += TryPatchMethod(
                harmony,
                "TaleWorlds.MountAndBlade.View.MissionViews.Sound.MusicBattleMissionView",
                "AfterStart",
                nameof(MusicBattleMissionView_AfterStart_Prefix)) ? 1 : 0;
            patchedCount += TryPatchMethod(
                harmony,
                "TaleWorlds.MountAndBlade.View.MissionViews.Sound.MusicBattleMissionView",
                "OnMissionScreenFinalize",
                nameof(MusicBattleMissionView_OnMissionScreenFinalize_Prefix)) ? 1 : 0;
            bool preloadViewGuardPatched = TryEnsureMissionPreloadViewPatch("ApplyClientMissionLoadingDiagnosticsOnly");
            patchedCount += preloadViewGuardPatched ? 1 : 0;
            if (preloadViewGuardPatched)
                ModLogger.Info("BattleShellSuppressionPatch: client mission-loading diagnostics-only ensured MissionPreloadView guard.");
            ModLogger.Info("BattleShellSuppressionPatch: client mission-loading diagnostics-only patch pass completed. SuccessfulPatches=" + patchedCount + ".");
        }

        public static bool IsNativeBattleShellSuppressionRuntime(Mission mission)
        {
            return IsCoopBattleMapRuntime(mission);
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

        private static bool TryPatchPostfixMethod(Harmony harmony, string typeName, string methodName, string postfixMethodName, params Type[] parameterTypes)
        {
            try
            {
                return PatchPostfixMethod(harmony, typeName, methodName, postfixMethodName, parameterTypes);
            }
            catch (Exception ex)
            {
                ModLogger.Error("BattleShellSuppressionPatch: failed to patch postfix " + typeName + "." + methodName + ".", ex);
                return false;
            }
        }

        private static bool TryEnsureMissionPreloadViewPatch(string source)
        {
            if (_missionPreloadViewPatchApplied)
                return false;

            if (_runtimeHarmony == null)
                return false;

            bool patched = TryPatchMethod(
                _runtimeHarmony,
                "SandBox.View.Missions.MissionPreloadView",
                "OnPreMissionTick",
                nameof(MissionPreloadView_OnPreMissionTick_Prefix),
                typeof(float));
            if (!patched)
                return false;

            _missionPreloadViewPatchApplied = true;
            ModLogger.Info("BattleShellSuppressionPatch: ensured MissionPreloadView.OnPreMissionTick patch. Source=" + source + ".");
            return true;
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

        private static bool PatchPostfixMethod(Harmony harmony, string typeName, string methodName, string postfixMethodName, params Type[] parameterTypes)
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
            MethodInfo postfix = typeof(BattleShellSuppressionPatch).GetMethod(postfixMethodName, BindingFlags.Static | BindingFlags.NonPublic);
            if (target == null || postfix == null)
            {
                ModLogger.Info("BattleShellSuppressionPatch: postfix method not found. Type=" + typeName + " Method=" + methodName);
                return false;
            }

            harmony.Patch(target, postfix: new HarmonyMethod(postfix));
            ModLogger.Info("BattleShellSuppressionPatch: patched postfix " + typeName + "." + methodName + ".");
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

            TryEnsureMissionPreloadViewPatch("MissionState.LoadMission");
            EnsureMissionScreenPreLoadBehaviorPatches(__instance);
            LogMissionBehaviorPreloadStack(__instance);
            LogMissionStateLoaderObservation(__instance, "MissionState.LoadMission");
            return true;
        }

        private static void MissionState_LoadMission_Postfix(object __instance)
        {
            LogMissionStateLoaderObservation(__instance, "MissionState.LoadMission completed");
        }

        private static void MissionState_LoadMission_DiagnosticsOnly_Prefix(object __instance)
        {
            LogMissionBehaviorPreloadStack(__instance);
            LogMissionStateLoaderObservation(__instance, "MissionState.LoadMission");
        }

        private static void MissionState_LoadMission_DiagnosticsOnly_Postfix(object __instance)
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

        private static void MissionBehavior_OnMissionScreenPreLoad_DiagnosticsOnly_Prefix(object __instance)
        {
            Mission mission = (__instance as MissionBehavior)?.Mission ?? Mission.Current;
            LogMissionBehaviorPreloadEntry(mission, __instance?.GetType());
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

        private static void Mission_AfterStart_Postfix(Mission __instance)
        {
            LogAfterStartPostfixObservation(__instance, "Mission.AfterStart completed");
        }

        private static void MissionState_FinishMissionLoading_Prefix(object __instance)
        {
            TryEnsureMissionPreloadViewPatch("MissionState.FinishMissionLoading");

            try
            {
                Mission mission = __instance?.GetType().GetProperty("CurrentMission", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(__instance) as Mission;
                if (!ShouldLogMissionStartupObservation(mission))
                    return;

                string sceneName = mission.SceneName ?? string.Empty;
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

        private static void MissionState_FinishMissionLoading_DiagnosticsOnly_Prefix(object __instance)
        {
            try
            {
                Mission mission = __instance?.GetType().GetProperty("CurrentMission", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(__instance) as Mission;
                TryRemoveRemoteClientSiegeCampaignOnlyMissionViews(mission, "MissionState.FinishMissionLoading diagnostics-only");
                if (!ShouldLogMissionStartupObservation(mission))
                    return;

                string sceneName = mission.SceneName ?? string.Empty;
                string key = sceneName + "|" + mission.Mode + "|" + mission.CurrentState + "|diagnostics-only";
                if (string.Equals(_lastFinishMissionLoadingObservationKey, key, StringComparison.Ordinal))
                    return;

                _lastFinishMissionLoadingObservationKey = key;
                ModLogger.Info(
                    "BattleShellSuppressionPatch: observed MissionState.FinishMissionLoading entry. " +
                    "Source=diagnostics-only" +
                    " Scene=" + sceneName +
                    " Mode=" + mission.Mode +
                    " MissionState=" + mission.CurrentState +
                    " HasLobbyComponent=" + (mission.GetMissionBehavior<MissionLobbyComponent>() != null) +
                    " HasMultiplayerRoundController=" + (mission.GetMissionBehavior<MultiplayerRoundController>() != null) +
                    " HasMissionMultiplayerFlagDomination=" + (mission.GetMissionBehavior<MissionMultiplayerFlagDomination>() != null) +
                    " HasMultiplayerWarmupComponent=" + (mission.GetMissionBehavior<MultiplayerWarmupComponent>() != null) + ".");
            }
            catch (Exception ex)
            {
                ModLogger.Info("BattleShellSuppressionPatch: diagnostics-only MissionState.FinishMissionLoading observation failed: " + ex.Message);
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

        private static void Mission_OnPreTick_Prefix(Mission __instance, float dt)
        {
            TryRemoveMissionPreloadViewForSiegeReplay(__instance, "Mission.OnPreTick");
        }

        private static void MissionLobbyComponent_AfterStart_Prefix(object __instance)
        {
            LogOfficialBattleStartupObservation((__instance as MissionBehavior)?.Mission ?? Mission.Current, "MissionLobbyComponent.AfterStart");
        }

        private static void MissionLobbyComponent_AfterStart_Postfix(object __instance)
        {
            LogAfterStartPostfixObservation((__instance as MissionBehavior)?.Mission ?? Mission.Current, "MissionLobbyComponent.AfterStart completed");
        }

        private static void MissionCustomGameServerComponent_AfterStart_Prefix(object __instance)
        {
            LogOfficialBattleStartupObservation((__instance as MissionBehavior)?.Mission ?? Mission.Current, "MissionCustomGameServerComponent.AfterStart");
        }

        private static void MissionCustomGameServerComponent_AfterStart_Postfix(object __instance)
        {
            LogAfterStartPostfixObservation((__instance as MissionBehavior)?.Mission ?? Mission.Current, "MissionCustomGameServerComponent.AfterStart completed");
        }

        private static void MissionScoreboardComponent_AfterStart_Prefix(object __instance)
        {
            LogOfficialBattleStartupObservation((__instance as MissionBehavior)?.Mission ?? Mission.Current, "MissionScoreboardComponent.AfterStart");
        }

        private static void MissionScoreboardComponent_AfterStart_Postfix(object __instance)
        {
            LogAfterStartPostfixObservation((__instance as MissionBehavior)?.Mission ?? Mission.Current, "MissionScoreboardComponent.AfterStart completed");
        }

        private static void MultiplayerTeamSelectComponent_AfterStart_Prefix(object __instance)
        {
            LogOfficialBattleStartupObservation((__instance as MissionBehavior)?.Mission ?? Mission.Current, "MultiplayerTeamSelectComponent.AfterStart");
        }

        private static void MultiplayerTeamSelectComponent_AfterStart_Postfix(object __instance)
        {
            LogAfterStartPostfixObservation((__instance as MissionBehavior)?.Mission ?? Mission.Current, "MultiplayerTeamSelectComponent.AfterStart completed");
        }

        private static void MissionLobbyComponent_EarlyStart_Prefix(object __instance)
        {
            Mission mission = (__instance as MissionBehavior)?.Mission ?? Mission.Current;
            TryEnsureSiegeNativeOpposingTeamsBeforeLobbyEarlyStart(mission);
            LogSiegeLobbyEarlyStartDiagnostics(mission);
        }

        private static void MissionTeamCollection_Add_Prefix(
            object __instance,
            BattleSideEnum side,
            uint color,
            uint color2,
            Banner banner,
            bool isPlayerGeneral,
            bool isPlayerSergeant,
            bool isSettingRelations)
        {
            Mission mission = TryGetMissionFromTeamCollection(__instance) ?? Mission.Current;
            LogSiegeTeamAddDiagnostics(
                mission,
                side,
                color,
                color2,
                banner,
                isPlayerGeneral,
                isPlayerSergeant,
                isSettingRelations);
        }

        private static bool MissionPreloadView_OnPreMissionTick_Prefix(object __instance, float dt)
        {
            try
            {
                Mission mission = (__instance as MissionBehavior)?.Mission ?? Mission.Current;
                if (mission == null ||
                    !MissionMultiplayerCoopSiegeAssaultWithDeploymentMode.HasCoopSiegeRuntimeMarker(mission) ||
                    !SceneRuntimeClassifier.IsSceneAwareBattleRuntimeScene(mission.SceneName ?? string.Empty))
                {
                    return true;
                }

                if (IsCampaignPlayerMapEventAvailable())
                    return true;

                string key = (mission.SceneName ?? "null") + "|" + mission.CurrentState + "|" + mission.Mode;
                if (!string.Equals(_lastMissionPreloadViewSkipKey, key, StringComparison.Ordinal))
                {
                    _lastMissionPreloadViewSkipKey = key;
                    ModLogger.Info(
                        "BattleShellSuppressionPatch: skipped MissionPreloadView.OnPreMissionTick for siege replay because " +
                        "MapEvent.PlayerMapEvent is null. Scene=" + (mission.SceneName ?? "null") +
                        " Mode=" + mission.Mode +
                        " MissionState=" + mission.CurrentState + ".");
                }

                return false;
            }
            catch (Exception ex)
            {
                ModLogger.Info("BattleShellSuppressionPatch: MissionPreloadView.OnPreMissionTick guard failed: " + ex.Message);
                return true;
            }
        }

        private static bool MusicBattleMissionView_AfterStart_Prefix(object __instance)
        {
            try
            {
                Mission mission = (__instance as MissionBehavior)?.Mission ?? Mission.Current;
                if (!ShouldSuppressRemoteClientSiegeMusicBattleMissionViewAfterStart(mission))
                    return true;

                string key = (mission.SceneName ?? "null") + "|" + mission.CurrentState + "|" + mission.Mode;
                if (!string.Equals(_lastMusicBattleMissionViewAfterStartSuppressionKey, key, StringComparison.Ordinal))
                {
                    _lastMusicBattleMissionViewAfterStartSuppressionKey = key;
                    ModLogger.Info(
                        "BattleShellSuppressionPatch: suppressed MusicBattleMissionView.AfterStart for remote-client siege replay. " +
                        "Scene=" + (mission.SceneName ?? "null") +
                        " Mode=" + mission.Mode +
                        " MissionState=" + mission.CurrentState + ".");
                }

                return false;
            }
            catch (Exception ex)
            {
                ModLogger.Info("BattleShellSuppressionPatch: MusicBattleMissionView.AfterStart guard failed: " + ex.Message);
                return true;
            }
        }

        private static bool MusicBattleMissionView_OnMissionScreenFinalize_Prefix(object __instance)
        {
            try
            {
                Mission mission = (__instance as MissionBehavior)?.Mission ?? Mission.Current;
                if (!ShouldSuppressRemoteClientSiegeMusicBattleMissionViewAfterStart(mission))
                    return true;

                try
                {
                    MBMusicManager.Current.DeactivateBattleMode();
                    MBMusicManager.Current.OnBattleMusicHandlerFinalize();
                }
                catch (Exception musicEx)
                {
                    ModLogger.Info("BattleShellSuppressionPatch: MusicBattleMissionView safe finalize music cleanup failed: " + musicEx.Message);
                }

                string key = (mission.SceneName ?? "null") + "|" + mission.CurrentState + "|" + mission.Mode;
                if (!string.Equals(_lastMusicBattleMissionViewFinalizeSuppressionKey, key, StringComparison.Ordinal))
                {
                    _lastMusicBattleMissionViewFinalizeSuppressionKey = key;
                    ModLogger.Info(
                        "BattleShellSuppressionPatch: safely finalized MusicBattleMissionView for remote-client siege replay without PlayerOrderController access. " +
                        "Scene=" + (mission.SceneName ?? "null") +
                        " Mode=" + mission.Mode +
                        " MissionState=" + mission.CurrentState + ".");
                }

                return false;
            }
            catch (Exception ex)
            {
                ModLogger.Info("BattleShellSuppressionPatch: MusicBattleMissionView.OnMissionScreenFinalize guard failed: " + ex.Message);
                return true;
            }
        }

        private static bool ShouldSuppressRemoteClientSiegeMusicBattleMissionViewAfterStart(Mission mission)
        {
            if (!GameNetwork.IsClient || GameNetwork.IsServer || mission == null)
                return false;

            if (!MissionMultiplayerCoopSiegeAssaultWithDeploymentMode.HasCoopSiegeRuntimeMarker(mission))
                return false;

            if (!SceneRuntimeClassifier.IsSceneAwareBattleRuntimeScene(mission.SceneName ?? string.Empty))
                return false;

            BattleScenarioContextMessage scenarioContext =
                BattleSnapshotRuntimeState.GetCurrent()?.ScenarioContext ??
                BattleSnapshotRuntimeState.GetState()?.ScenarioContext;
            return ExactCampaignSiegeAssaultWithDeploymentRuntime.IsSiegeAssaultScenario(scenarioContext);
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
            if (ShouldSuppressDedicatedSiegeWarmupAfterStart(__instance))
                return false;

            return !ShouldSuppressNativeBattleShell(__instance, "MultiplayerWarmupComponent.AfterStart");
        }

        private static bool MultiplayerWarmupComponent_OnPreDisplayMissionTick_Prefix(object __instance, float dt)
        {
            if (ShouldSuppressDedicatedSiegeWarmupPreDisplay(__instance, dt))
                return false;

            return !ShouldSuppressNativeBattleShell(__instance, "MultiplayerWarmupComponent.OnPreDisplayMissionTick");
        }

        private static bool WarmupSpawningBehavior_OnTick_Prefix(object __instance, float dt)
        {
            try
            {
                if (ShouldSuppressDedicatedSiegeWarmupSpawningTickAfterBattleEnd(__instance, dt))
                    return false;
            }
            catch (Exception ex)
            {
                ModLogger.Info("BattleShellSuppressionPatch: WarmupSpawningBehavior.OnTick guard failed open: " + ex.Message);
            }

            return true;
        }

        private static bool MultiplayerWarmupComponent_HandleNewClientAfterSynchronized_Prefix(object __instance, object networkPeer)
        {
            return !ShouldSuppressNativeBattleShell(__instance, "MultiplayerWarmupComponent.HandleNewClientAfterSynchronized");
        }

        private static bool MultiplayerTimerComponent_StartTimerAsServer_Prefix(object __instance, float duration)
        {
            if (ShouldSuppressDedicatedSiegeTimerStartAsServer(__instance, duration))
                return false;

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

        private static bool Team_Tick_Prefix(object __instance, float dt)
        {
            try
            {
                Team team = __instance as Team;
                Mission mission = team?.Mission ?? Mission.Current;
                if (!ShouldSuppressDedicatedSiegeTeamTickBeforeBattle(mission, team))
                    return true;

                string key =
                    (mission.SceneName ?? "unknown") + "|" +
                    SafeMissionModeName(mission) + "|" +
                    mission.CurrentState + "|" +
                    CoopBattlePhaseRuntimeState.GetPhase();
                if (!string.Equals(_lastDedicatedSiegeTeamTickSuppressionKey, key, StringComparison.Ordinal))
                {
                    _lastDedicatedSiegeTeamTickSuppressionKey = key;
                    ModLogger.Info(
                        "BattleShellSuppressionPatch: suppressed dedicated siege replay Team.Tick before battle start. " +
                        "Scene=" + (mission.SceneName ?? "unknown") +
                        " Mode=" + SafeMissionModeName(mission) +
                        " MissionState=" + mission.CurrentState +
                        " BattlePhase=" + CoopBattlePhaseRuntimeState.GetPhase() +
                        " Dt=" + dt.ToString("0.0000") +
                        " TeamSide=" + team.Side +
                        " TeamIndex=" + team.TeamIndex + ".");
                }

                return false;
            }
            catch (Exception ex)
            {
                ModLogger.Info("BattleShellSuppressionPatch: Team.Tick guard failed: " + ex.Message);
                return true;
            }
        }

        private static bool ShouldSuppressDedicatedSiegeWarmupAfterStart(object instance)
        {
            Mission mission = (instance as MissionBehavior)?.Mission ?? Mission.Current;
            if (!GameNetwork.IsServer || mission == null || !IsDedicatedServerProcess())
                return false;

            if (!IsCoopBattleMapRuntime(mission) ||
                mission.GetMissionBehavior<MissionMultiplayerCoopSiegeAssaultWithDeployment>() == null)
            {
                return false;
            }

            BattleScenarioContextMessage scenarioContext =
                BattleSnapshotRuntimeState.GetCurrent()?.ScenarioContext ??
                BattleSnapshotRuntimeState.GetState()?.ScenarioContext;
            string siegeSubtype = scenarioContext?.SiegeContext?.SiegeSubtype ?? string.Empty;
            if (scenarioContext?.IsSiegeBattle != true ||
                !string.Equals(siegeSubtype, "SiegeAssault", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string modeName = SafeMissionModeName(mission);
            if (!string.Equals(modeName, "Deployment", StringComparison.OrdinalIgnoreCase))
                return false;

            string key =
                (mission.SceneName ?? "unknown") + "|" +
                modeName + "|" +
                mission.CurrentState + "|" +
                CoopBattlePhaseRuntimeState.GetPhase();
            if (!string.Equals(_lastDedicatedSiegeWarmupAfterStartSuppressionKey, key, StringComparison.Ordinal))
            {
                _lastDedicatedSiegeWarmupAfterStartSuppressionKey = key;
                ModLogger.Info(
                    "BattleShellSuppressionPatch: suppressed dedicated siege replay MultiplayerWarmupComponent.AfterStart native original during deployment. " +
                    "Scene=" + (mission.SceneName ?? "unknown") +
                    " Mode=" + modeName +
                    " MissionState=" + mission.CurrentState +
                    " BattlePhase=" + CoopBattlePhaseRuntimeState.GetPhase() +
                    " HasLobbyComponent=" + (mission.GetMissionBehavior<MissionLobbyComponent>() != null) +
                    " HasTimerComponent=" + (mission.GetMissionBehavior<MultiplayerTimerComponent>() != null) +
                    " HasTeamSelectComponent=" + (mission.GetMissionBehavior<MultiplayerTeamSelectComponent>() != null) + ".");
            }

            return true;
        }

        private static bool ShouldSuppressDedicatedSiegeTeamTickBeforeBattle(Mission mission, Team team)
        {
            if (!GameNetwork.IsServer || mission == null || team == null || !IsDedicatedServerProcess())
                return false;

            if (team.Side == BattleSideEnum.None || ReferenceEquals(team, mission.SpectatorTeam))
                return false;

            if (!IsCoopBattleMapRuntime(mission) ||
                mission.GetMissionBehavior<MissionMultiplayerCoopSiegeAssaultWithDeployment>() == null)
            {
                return false;
            }

            CoopBattlePhase currentPhase = CoopBattlePhaseRuntimeState.GetPhase();
            if (currentPhase < CoopBattlePhase.PreBattleHold || currentPhase >= CoopBattlePhase.BattleActive)
                return false;

            BattleScenarioContextMessage scenarioContext =
                BattleSnapshotRuntimeState.GetCurrent()?.ScenarioContext ??
                BattleSnapshotRuntimeState.GetState()?.ScenarioContext;
            return ExactCampaignSiegeAssaultWithDeploymentRuntime.IsSiegeAssaultScenario(scenarioContext);
        }

        private static bool ShouldSuppressDedicatedSiegeWarmupSpawningTickAfterBattleEnd(object instance, float dt)
        {
            Mission mission = (instance as MissionBehavior)?.Mission ?? Mission.Current;
            if (!GameNetwork.IsServer || mission == null || !IsDedicatedServerProcess())
                return false;

            if (!IsCoopBattleMapRuntime(mission) ||
                mission.GetMissionBehavior<MissionMultiplayerCoopSiegeAssaultWithDeployment>() == null)
            {
                return false;
            }

            CoopBattlePhase currentPhase = CoopBattlePhaseRuntimeState.GetPhase();
            if (currentPhase < CoopBattlePhase.BattleEnded)
                return false;

            BattleScenarioContextMessage scenarioContext =
                BattleSnapshotRuntimeState.GetCurrent()?.ScenarioContext ??
                BattleSnapshotRuntimeState.GetState()?.ScenarioContext;
            if (!ExactCampaignSiegeAssaultWithDeploymentRuntime.IsSiegeAssaultScenario(scenarioContext))
                return false;

            string key =
                (mission.SceneName ?? "unknown") + "|" +
                SafeMissionModeName(mission) + "|" +
                mission.CurrentState + "|" +
                currentPhase;
            if (!string.Equals(_lastDedicatedSiegeWarmupSpawningTickSuppressionKey, key, StringComparison.Ordinal))
            {
                _lastDedicatedSiegeWarmupSpawningTickSuppressionKey = key;
                ModLogger.Info(
                    "BattleShellSuppressionPatch: suppressed dedicated siege replay WarmupSpawningBehavior.OnTick after battle end. " +
                    "Scene=" + (mission.SceneName ?? "unknown") +
                    " Mode=" + SafeMissionModeName(mission) +
                    " MissionState=" + mission.CurrentState +
                    " BattlePhase=" + currentPhase +
                    " Dt=" + dt.ToString("0.0000") + ".");
            }

            return true;
        }

        private static bool ShouldSuppressDedicatedSiegeWarmupPreDisplay(object instance, float dt)
        {
            Mission mission = (instance as MissionBehavior)?.Mission ?? Mission.Current;
            if (!IsDedicatedSiegeReplayDeploymentInitializing(mission))
                return false;

            string key =
                (mission.SceneName ?? "unknown") + "|" +
                SafeMissionModeName(mission) + "|" +
                mission.CurrentState + "|" +
                CoopBattlePhaseRuntimeState.GetPhase();
            if (!string.Equals(_lastDedicatedSiegeWarmupPreDisplaySuppressionKey, key, StringComparison.Ordinal))
            {
                _lastDedicatedSiegeWarmupPreDisplaySuppressionKey = key;
                ModLogger.Info(
                    "BattleShellSuppressionPatch: suppressed dedicated siege replay MultiplayerWarmupComponent.OnPreDisplayMissionTick during deployment initialization. " +
                    "Scene=" + (mission.SceneName ?? "unknown") +
                    " Mode=" + SafeMissionModeName(mission) +
                    " MissionState=" + mission.CurrentState +
                    " BattlePhase=" + CoopBattlePhaseRuntimeState.GetPhase() +
                    " Dt=" + dt.ToString("0.0000") +
                    " HasLobbyComponent=" + (mission.GetMissionBehavior<MissionLobbyComponent>() != null) +
                    " HasTimerComponent=" + (mission.GetMissionBehavior<MultiplayerTimerComponent>() != null) +
                    " HasTeamSelectComponent=" + (mission.GetMissionBehavior<MultiplayerTeamSelectComponent>() != null) + ".");
            }

            return true;
        }

        private static bool ShouldSuppressDedicatedSiegeTimerStartAsServer(object instance, float duration)
        {
            Mission mission = (instance as MissionBehavior)?.Mission ?? Mission.Current;
            if (!IsDedicatedSiegeReplayDeploymentInitializing(mission))
                return false;

            string key =
                (mission.SceneName ?? "unknown") + "|" +
                SafeMissionModeName(mission) + "|" +
                mission.CurrentState + "|" +
                CoopBattlePhaseRuntimeState.GetPhase() + "|" +
                duration.ToString("0.0000");
            if (!string.Equals(_lastDedicatedSiegeTimerStartSuppressionKey, key, StringComparison.Ordinal))
            {
                _lastDedicatedSiegeTimerStartSuppressionKey = key;
                ModLogger.Info(
                    "BattleShellSuppressionPatch: suppressed dedicated siege replay MultiplayerTimerComponent.StartTimerAsServer during deployment initialization. " +
                    "Scene=" + (mission.SceneName ?? "unknown") +
                    " Mode=" + SafeMissionModeName(mission) +
                    " MissionState=" + mission.CurrentState +
                    " BattlePhase=" + CoopBattlePhaseRuntimeState.GetPhase() +
                    " Duration=" + duration.ToString("0.0000") +
                    " HasLobbyComponent=" + (mission.GetMissionBehavior<MissionLobbyComponent>() != null) +
                    " HasWarmupComponent=" + (mission.GetMissionBehavior<MultiplayerWarmupComponent>() != null) +
                    " HasTeamSelectComponent=" + (mission.GetMissionBehavior<MultiplayerTeamSelectComponent>() != null) + ".");
            }

            return true;
        }

        private static bool IsDedicatedSiegeReplayDeploymentInitializing(Mission mission)
        {
            if (!GameNetwork.IsServer || mission == null || !IsDedicatedServerProcess())
                return false;

            if (!IsCoopBattleMapRuntime(mission) ||
                mission.GetMissionBehavior<MissionMultiplayerCoopSiegeAssaultWithDeployment>() == null)
            {
                return false;
            }

            BattleScenarioContextMessage scenarioContext =
                BattleSnapshotRuntimeState.GetCurrent()?.ScenarioContext ??
                BattleSnapshotRuntimeState.GetState()?.ScenarioContext;
            string siegeSubtype = scenarioContext?.SiegeContext?.SiegeSubtype ?? string.Empty;
            if (scenarioContext?.IsSiegeBattle != true ||
                !string.Equals(siegeSubtype, "SiegeAssault", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!string.Equals(SafeMissionModeName(mission), "Deployment", StringComparison.OrdinalIgnoreCase))
                return false;

            Mission.State missionState = mission.CurrentState;
            return missionState == Mission.State.NewlyCreated ||
                   missionState == Mission.State.Initializing;
        }

        private static bool ShouldSuppressNativeBattleShell(object instance, string source)
        {
            Mission mission = (instance as MissionBehavior)?.Mission ?? Mission.Current;
            if (!IsCoopBattleMapRuntime(mission))
                return false;

            if (ShouldAllowNativeSiegeAssaultStartupPath(mission, source))
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

            if (GameNetwork.IsClient &&
                !GameNetwork.IsServer &&
                !CoopMissionNetworkBridge.IsClientCurrentBattleSnapshotApplied(out string snapshotReadinessSummary))
            {
                string passThroughKey =
                    (source ?? "unknown") + "|" +
                    (mission?.SceneName ?? "unknown") + "|" +
                    (snapshotReadinessSummary ?? "unknown");
                if (!string.Equals(_lastClientBootstrapPassThroughLogKey, passThroughKey, StringComparison.Ordinal))
                {
                    _lastClientBootstrapPassThroughLogKey = passThroughKey;
                    ModLogger.Info(
                        "BattleShellSuppressionPatch: allowed native battle shell path while client battle snapshot bootstrap is pending. " +
                        "Source=" + (source ?? "unknown") +
                        " Scene=" + (mission?.SceneName ?? "unknown") +
                        " SnapshotReadiness=" + (snapshotReadinessSummary ?? "unknown") + ".");
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
                    " HasCoopSiegeServer=" + (mission?.GetMissionBehavior<MissionMultiplayerCoopSiegeAssaultWithDeployment>() != null) +
                    " HasCoopSiegeClient=" + (mission?.GetMissionBehavior<MissionMultiplayerCoopSiegeAssaultWithDeploymentClient>() != null) +
                    " HasCoopSpawnLogic=" + (mission?.GetMissionBehavior<CoopMissionSpawnLogic>() != null) + ".");
            }

            return true;
        }

        private static bool ShouldAllowNativeSiegeAssaultStartupPath(Mission mission, string source)
        {
            if (!GameNetwork.IsServer || mission == null || !IsDedicatedServerProcess())
                return false;

            BattleScenarioContextMessage scenarioContext =
                BattleSnapshotRuntimeState.GetCurrent()?.ScenarioContext ??
                BattleSnapshotRuntimeState.GetState()?.ScenarioContext;
            string siegeSubtype = scenarioContext?.SiegeContext?.SiegeSubtype ?? string.Empty;
            if (scenarioContext?.IsSiegeBattle != true ||
                !string.Equals(siegeSubtype, "SiegeAssault", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!IsEarlyNativeSiegeStartupWindow(mission))
                return false;

            string modeName = SafeMissionModeName(mission);
            string key =
                (source ?? "unknown") + "|" +
                (mission.SceneName ?? "unknown") + "|" +
                modeName + "|" +
                mission.CurrentState;
            if (!string.Equals(_lastSiegeStartupPassThroughLogKey, key, StringComparison.Ordinal))
            {
                _lastSiegeStartupPassThroughLogKey = key;
                ModLogger.Info(
                    "BattleShellSuppressionPatch: allowed native siege-assault startup path before coop shell suppression. " +
                    "Source=" + (source ?? "unknown") +
                    " Scene=" + (mission.SceneName ?? "unknown") +
                    " Mode=" + modeName +
                    " MissionState=" + mission.CurrentState +
                    " BattlePhase=" + CoopBattlePhaseRuntimeState.GetPhase() +
                    " HasLobbyComponent=" + (mission.GetMissionBehavior<MissionLobbyComponent>() != null) +
                    " HasTimerComponent=" + (mission.GetMissionBehavior<MultiplayerTimerComponent>() != null) +
                    " HasTeamSelectComponent=" + (mission.GetMissionBehavior<MultiplayerTeamSelectComponent>() != null) + ".");
            }

            return true;
        }

        private static bool IsEarlyNativeSiegeStartupWindow(Mission mission)
        {
            if (mission == null)
                return false;

            Mission.State missionState = mission.CurrentState;
            return missionState == Mission.State.NewlyCreated ||
                   missionState == Mission.State.Initializing;
        }

        private static string SafeMissionModeName(Mission mission)
        {
            if (mission == null)
                return string.Empty;

            try
            {
                return mission.Mode.ToString();
            }
            catch
            {
                return string.Empty;
            }
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
                || mission.GetMissionBehavior<MissionMultiplayerCoopSiegeAssaultWithDeployment>() != null
                || mission.GetMissionBehavior<MissionMultiplayerCoopSiegeAssaultWithDeploymentClient>() != null
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
                if (!ShouldLogMissionStartupObservation(mission))
                    return;

                string sceneName = mission.SceneName ?? string.Empty;
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
                    " HasMultiplayerTimerComponent=" + (mission.GetMissionBehavior<MultiplayerTimerComponent>() != null) +
                    " HasCoopSiegeServer=" + (mission.GetMissionBehavior<MissionMultiplayerCoopSiegeAssaultWithDeployment>() != null) +
                    " HasCoopSiegeClient=" + (mission.GetMissionBehavior<MissionMultiplayerCoopSiegeAssaultWithDeploymentClient>() != null) + ".");
            }
            catch (Exception ex)
            {
                ModLogger.Info("BattleShellSuppressionPatch: official battle startup observation failed: " + ex.Message);
            }
        }

        private static void LogAfterStartPostfixObservation(Mission mission, string source)
        {
            try
            {
                if (!ShouldLogMissionStartupObservation(mission))
                    return;

                string sceneName = mission.SceneName ?? string.Empty;
                string key =
                    (source ?? "unknown") + "|" +
                    sceneName + "|" +
                    mission.Mode + "|" +
                    mission.CurrentState + "|" +
                    CoopBattlePhaseRuntimeState.GetPhase() + "|" +
                    (mission.GetMissionBehavior<MissionMultiplayerCoopSiegeAssaultWithDeployment>() != null);
                if (string.Equals(_lastAfterStartPostfixObservationKey, key, StringComparison.Ordinal))
                    return;

                _lastAfterStartPostfixObservationKey = key;
                ModLogger.Info(
                    "BattleShellSuppressionPatch: observed AfterStart postfix boundary. " +
                    "Source=" + (source ?? "unknown") +
                    " Scene=" + sceneName +
                    " Mode=" + mission.Mode +
                    " MissionState=" + mission.CurrentState +
                    " BattlePhase=" + CoopBattlePhaseRuntimeState.GetPhase() +
                    " HasLobbyComponent=" + (mission.GetMissionBehavior<MissionLobbyComponent>() != null) +
                    " HasMissionNetworkComponent=" + (mission.GetMissionBehavior<MissionNetworkComponent>() != null) +
                    " HasMissionCustomGameServerComponent=" + HasMissionBehaviorTypeName(mission, "MissionCustomGameServerComponent") +
                    " HasMissionLobbyEquipmentNetworkComponent=" + (mission.GetMissionBehavior<MissionLobbyEquipmentNetworkComponent>() != null) +
                    " HasWarmupComponent=" + (mission.GetMissionBehavior<MultiplayerWarmupComponent>() != null) +
                    " HasTimerComponent=" + (mission.GetMissionBehavior<MultiplayerTimerComponent>() != null) +
                    " HasTeamSelectComponent=" + (mission.GetMissionBehavior<MultiplayerTeamSelectComponent>() != null) +
                    " HasCoopSiegeServer=" + (mission.GetMissionBehavior<MissionMultiplayerCoopSiegeAssaultWithDeployment>() != null) +
                    " HasCoopSiegeClient=" + (mission.GetMissionBehavior<MissionMultiplayerCoopSiegeAssaultWithDeploymentClient>() != null) + ".");
            }
            catch (Exception ex)
            {
                ModLogger.Info("BattleShellSuppressionPatch: AfterStart postfix observation failed: " + ex.Message);
            }
        }

        private static bool HasMissionBehaviorTypeName(Mission mission, string behaviorTypeName)
        {
            if (mission?.MissionBehaviors == null || string.IsNullOrWhiteSpace(behaviorTypeName))
                return false;

            foreach (MissionBehavior behavior in mission.MissionBehaviors)
            {
                Type type = behavior?.GetType();
                if (type == null)
                    continue;

                if (string.Equals(type.Name, behaviorTypeName, StringComparison.Ordinal) ||
                    string.Equals(type.FullName, behaviorTypeName, StringComparison.Ordinal) ||
                    (type.FullName != null && type.FullName.EndsWith("." + behaviorTypeName, StringComparison.Ordinal)))
                {
                    return true;
                }
            }

            return false;
        }

        private static Mission TryGetMissionFromTeamCollection(object teamCollection)
        {
            try
            {
                if (teamCollection == null)
                    return null;

                FieldInfo missionField = teamCollection
                    .GetType()
                    .GetField("_mission", BindingFlags.Instance | BindingFlags.NonPublic);
                return missionField?.GetValue(teamCollection) as Mission;
            }
            catch (Exception ex)
            {
                ModLogger.Info("BattleShellSuppressionPatch: siege team-add diagnostics could not resolve TeamCollection mission: " + ex.Message);
                return null;
            }
        }

        private static void LogSiegeLobbyEarlyStartDiagnostics(Mission mission)
        {
            try
            {
                if (!ShouldLogSiegeTeamAddDiagnostics(mission))
                    return;

                string key = BuildSiegeTeamDiagnosticsKey(mission, "MissionLobbyComponent.EarlyStart");
                if (string.Equals(_lastSiegeLobbyEarlyStartDiagnosticsKey, key, StringComparison.Ordinal))
                    return;

                _lastSiegeLobbyEarlyStartDiagnosticsKey = key;
                ModLogger.Info(
                    "BattleShellSuppressionPatch: observed siege replay MissionLobbyComponent.EarlyStart before native spectator team add. " +
                    BuildSiegeTeamStateSummary(mission));
            }
            catch (Exception ex)
            {
                ModLogger.Info("BattleShellSuppressionPatch: siege lobby EarlyStart diagnostics failed: " + ex.Message);
            }
        }

        private static void LogSiegeTeamAddDiagnostics(
            Mission mission,
            BattleSideEnum side,
            uint color,
            uint color2,
            Banner banner,
            bool isPlayerGeneral,
            bool isPlayerSergeant,
            bool isSettingRelations)
        {
            try
            {
                if (!ShouldLogSiegeTeamAddDiagnostics(mission))
                    return;

                string key =
                    BuildSiegeTeamDiagnosticsKey(mission, "Mission.TeamCollection.Add") +
                    "|" + side +
                    "|" + GetTeamCount(mission);
                if (string.Equals(_lastSiegeTeamAddDiagnosticsKey, key, StringComparison.Ordinal))
                    return;

                _lastSiegeTeamAddDiagnosticsKey = key;
                ModLogger.Info(
                    "BattleShellSuppressionPatch: observed siege replay Mission.TeamCollection.Add before native team creation. " +
                    "Side=" + side +
                    " Color=" + color +
                    " Color2=" + color2 +
                    " HasBanner=" + (banner != null) +
                    " IsPlayerGeneral=" + isPlayerGeneral +
                    " IsPlayerSergeant=" + isPlayerSergeant +
                    " IsSettingRelations=" + isSettingRelations +
                    " " + BuildSiegeTeamStateSummary(mission));
            }
            catch (Exception ex)
            {
                ModLogger.Info("BattleShellSuppressionPatch: siege TeamCollection.Add diagnostics failed: " + ex.Message);
            }
        }

        private static bool ShouldLogSiegeTeamAddDiagnostics(Mission mission)
        {
            if (!ExperimentalFeatures.EnableSiegeReplayTeamAddDiagnostics)
                return false;

            if (!GameNetwork.IsServer || mission == null)
                return false;

            if (!MissionMultiplayerCoopSiegeAssaultWithDeploymentMode.HasCoopSiegeRuntimeMarker(mission))
                return false;

            string sceneName = mission.SceneName ?? string.Empty;
            return SceneRuntimeClassifier.IsSceneAwareBattleRuntimeScene(sceneName);
        }

        private static void TryEnsureSiegeNativeOpposingTeamsBeforeLobbyEarlyStart(Mission mission)
        {
            try
            {
                if (!ShouldEnsureSiegeNativeOpposingTeamsBeforeLobbyEarlyStart(mission))
                    return;

                if (mission.Teams.Attacker != null && mission.Teams.Defender != null)
                {
                    TryEnsureSiegeReplayPlayerTeamBeforeDeployment(mission, "existing-teams");
                    return;
                }

                string attackerCultureId = ResolveSiegeTeamCultureId(BattleSideEnum.Attacker, "empire");
                string defenderCultureId = ResolveSiegeTeamCultureId(BattleSideEnum.Defender, "vlandia");
                BasicCultureObject attackerCulture = ResolveCulture(attackerCultureId);
                BasicCultureObject defenderCulture = ResolveCulture(defenderCultureId);
                MultiplayerBattleColors battleColors = MultiplayerBattleColors.CreateWith(attackerCulture, defenderCulture);
                BasicCultureObject effectiveAttackerCulture = battleColors.AttackerColors.Culture ?? attackerCulture;
                BasicCultureObject effectiveDefenderCulture = battleColors.DefenderColors.Culture ?? defenderCulture;

                ModLogger.Info(
                    "BattleShellSuppressionPatch: ensuring siege replay native opposing teams before MissionLobbyComponent.EarlyStart. " +
                    "AttackerCulture=" + (effectiveAttackerCulture?.StringId ?? attackerCultureId ?? "null") +
                    " DefenderCulture=" + (effectiveDefenderCulture?.StringId ?? defenderCultureId ?? "null") +
                    " Before=" + BuildSiegeTeamStateSummary(mission));

                if (mission.Teams.Attacker == null)
                {
                    Banner attackerBanner = TryCreateNativeSiegeBanner(
                        effectiveAttackerCulture,
                        battleColors.AttackerColors.BannerBackgroundColorUint,
                        battleColors.AttackerColors.BannerForegroundColorUint);
                    mission.Teams.Add(
                        BattleSideEnum.Attacker,
                        battleColors.AttackerColors.BannerBackgroundColorUint,
                        battleColors.AttackerColors.BannerForegroundColorUint,
                        attackerBanner);
                }

                if (mission.Teams.Defender == null)
                {
                    Banner defenderBanner = TryCreateNativeSiegeBanner(
                        effectiveDefenderCulture,
                        battleColors.DefenderColors.BannerBackgroundColorUint,
                        battleColors.DefenderColors.BannerForegroundColorUint);
                    mission.Teams.Add(
                        BattleSideEnum.Defender,
                        battleColors.DefenderColors.BannerBackgroundColorUint,
                        battleColors.DefenderColors.BannerForegroundColorUint,
                        defenderBanner);
                }

                TryEnsureSiegeReplayPlayerTeamBeforeDeployment(mission, "native-team-bootstrap");

                ModLogger.Info(
                    "BattleShellSuppressionPatch: ensured siege replay native opposing teams before MissionLobbyComponent.EarlyStart. " +
                    BuildSiegeTeamStateSummary(mission));
            }
            catch (Exception ex)
            {
                ModLogger.Error(
                    "BattleShellSuppressionPatch: failed to ensure siege replay native opposing teams before MissionLobbyComponent.EarlyStart.",
                    ex);
            }
        }

        private static void TryEnsureSiegeReplayPlayerTeamBeforeDeployment(Mission mission, string source)
        {
            try
            {
                if (mission?.Teams == null || mission.Teams.Attacker == null || mission.Teams.Defender == null)
                    return;

                BattleSideEnum playerSide = ResolveSiegeReplayPlayerSide();
                Team playerTeam = playerSide == BattleSideEnum.Defender
                    ? mission.Teams.Defender
                    : mission.Teams.Attacker;

                if (playerTeam == null)
                    return;

                if (ReferenceEquals(mission.PlayerTeam, playerTeam) &&
                    mission.PlayerEnemyTeam != null)
                    return;

                Team previousPlayerTeam = mission.PlayerTeam;
                Team previousPlayerEnemyTeam = mission.PlayerEnemyTeam;
                mission.PlayerTeam = playerTeam;

                string key =
                    (mission.SceneName ?? "null") + "|" +
                    source + "|" +
                    playerSide + "|" +
                    (previousPlayerTeam?.TeamIndex.ToString() ?? "null") + "|" +
                    (mission.PlayerTeam?.TeamIndex.ToString() ?? "null") + "|" +
                    (mission.PlayerEnemyTeam?.TeamIndex.ToString() ?? "null");
                if (!string.Equals(_lastSiegePlayerTeamBootstrapKey, key, StringComparison.Ordinal))
                {
                    _lastSiegePlayerTeamBootstrapKey = key;
                    ModLogger.Info(
                        "BattleShellSuppressionPatch: ensured siege replay Mission.PlayerTeam before deployment. " +
                        "Source=" + source +
                        " PlayerSide=" + playerSide +
                        " PreviousPlayerTeam=" + FormatTeam(previousPlayerTeam) +
                        " PreviousPlayerEnemyTeam=" + FormatTeam(previousPlayerEnemyTeam) +
                        " AppliedPlayerTeam=" + FormatTeam(mission.PlayerTeam) +
                        " AppliedPlayerEnemyTeam=" + FormatTeam(mission.PlayerEnemyTeam) +
                        " " + BuildSiegeTeamStateSummary(mission));
                }
            }
            catch (Exception ex)
            {
                ModLogger.Error("BattleShellSuppressionPatch: failed to ensure siege replay Mission.PlayerTeam before deployment.", ex);
            }
        }

        private static BattleSideEnum ResolveSiegeReplayPlayerSide()
        {
            var runtimeSides = BattleSnapshotRuntimeState.GetState()?.Sides;
            if (runtimeSides != null)
            {
                foreach (BattleSideState side in runtimeSides)
                {
                    if (side == null || !side.IsPlayerSide)
                        continue;

                    return IsAttackerSideKey(side.CanonicalSideKey ?? side.SideId)
                        ? BattleSideEnum.Attacker
                        : BattleSideEnum.Defender;
                }
            }

            var snapshotSides = BattleSnapshotRuntimeState.GetCurrent()?.Sides;
            if (snapshotSides != null)
            {
                foreach (BattleSideSnapshotMessage side in snapshotSides)
                {
                    if (side == null || !side.IsPlayerSide)
                        continue;

                    return IsAttackerSideKey(side.SideText ?? side.SideId)
                        ? BattleSideEnum.Attacker
                        : BattleSideEnum.Defender;
                }
            }

            return BattleSideEnum.Attacker;
        }

        private static bool IsAttackerSideKey(string sideKey)
        {
            if (string.IsNullOrWhiteSpace(sideKey))
                return false;

            return sideKey.IndexOf("attacker", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   sideKey.IndexOf("attack", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   sideKey.IndexOf("team1", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   string.Equals(sideKey, "1", StringComparison.OrdinalIgnoreCase);
        }

        private static string FormatTeam(Team team)
        {
            return team == null
                ? "null"
                : team.Side + "#" + team.TeamIndex;
        }

        private static bool IsCampaignPlayerMapEventAvailable()
        {
            try
            {
                Type mapEventType = AccessTools.TypeByName("TaleWorlds.CampaignSystem.MapEvents.MapEvent");
                PropertyInfo playerMapEventProperty = mapEventType?.GetProperty(
                    "PlayerMapEvent",
                    BindingFlags.Public | BindingFlags.Static);
                return playerMapEventProperty?.GetValue(null, null) != null;
            }
            catch
            {
                return false;
            }
        }

        private static void TryRemoveMissionPreloadViewForSiegeReplay(Mission mission, string source)
        {
            try
            {
                if (GameNetwork.IsServer || mission == null)
                    return;

                if (!MissionMultiplayerCoopSiegeAssaultWithDeploymentMode.HasCoopSiegeRuntimeMarker(mission))
                    return;

                string sceneName = mission.SceneName ?? string.Empty;
                if (!SceneRuntimeClassifier.IsSceneAwareBattleRuntimeScene(sceneName))
                    return;

                if (IsCampaignPlayerMapEventAvailable())
                    return;

                List<MissionBehavior> missionBehaviors = mission.MissionBehaviors;
                if (missionBehaviors == null || missionBehaviors.Count == 0)
                    return;

                int removedCount = 0;
                for (int i = missionBehaviors.Count - 1; i >= 0; i--)
                {
                    MissionBehavior behavior = missionBehaviors[i];
                    Type behaviorType = behavior?.GetType();
                    string behaviorTypeName = behaviorType?.FullName ?? string.Empty;
                    if (!string.Equals(behaviorTypeName, "SandBox.View.Missions.MissionPreloadView", StringComparison.Ordinal) &&
                        !string.Equals(behaviorType?.Name, "MissionPreloadView", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    mission.RemoveMissionBehavior(behavior);
                    removedCount++;
                }

                if (removedCount <= 0)
                    return;

                string key = sceneName + "|" + mission.Mode + "|" + mission.CurrentState + "|" + source + "|" + removedCount;
                if (string.Equals(_lastMissionPreloadViewRemovalKey, key, StringComparison.Ordinal))
                    return;

                _lastMissionPreloadViewRemovalKey = key;
                ModLogger.Info(
                    "BattleShellSuppressionPatch: removed MissionPreloadView from siege replay client mission before native pre-tick. " +
                    "Source=" + source +
                    " Scene=" + sceneName +
                    " Mode=" + mission.Mode +
                    " MissionState=" + mission.CurrentState +
                    " RemovedCount=" + removedCount + ".");
            }
            catch (Exception ex)
            {
                ModLogger.Info("BattleShellSuppressionPatch: MissionPreloadView removal guard failed: " + ex.Message);
            }
        }

        private static void TryRemoveRemoteClientSiegeCampaignOnlyMissionViews(Mission mission, string source)
        {
            try
            {
                if (!ShouldSuppressRemoteClientSiegeSingleplayerMissionView(mission))
                    return;

                List<MissionBehavior> missionBehaviors = mission.MissionBehaviors;
                if (missionBehaviors == null || missionBehaviors.Count == 0)
                    return;

                List<string> removedTypeNames = new List<string>();
                for (int i = missionBehaviors.Count - 1; i >= 0; i--)
                {
                    MissionBehavior behavior = missionBehaviors[i];
                    Type behaviorType = behavior?.GetType();
                    string behaviorTypeName = behaviorType?.FullName ?? string.Empty;
                    string behaviorSimpleName = behaviorType?.Name ?? string.Empty;
                    if (!RemoteClientSiegeCampaignOnlyMissionViewTypeNames.Contains(behaviorTypeName) &&
                        !RemoteClientSiegeCampaignOnlyMissionViewTypeNames.Contains(behaviorSimpleName))
                    {
                        continue;
                    }

                    mission.RemoveMissionBehavior(behavior);
                    removedTypeNames.Add(behaviorSimpleName.Length > 0 ? behaviorSimpleName : behaviorTypeName);
                }

                if (removedTypeNames.Count <= 0)
                    return;

                string sceneName = mission.SceneName ?? string.Empty;
                string key = sceneName + "|" + mission.Mode + "|" + mission.CurrentState + "|" + source + "|" + removedTypeNames.Count;
                if (string.Equals(_lastRemoteClientSiegeCampaignOnlyViewRemovalKey, key, StringComparison.Ordinal))
                    return;

                _lastRemoteClientSiegeCampaignOnlyViewRemovalKey = key;
                ModLogger.Info(
                    "BattleShellSuppressionPatch: removed campaign-only mission views from siege replay client mission before screen initialize. " +
                    "Source=" + source +
                    " Scene=" + sceneName +
                    " Mode=" + mission.Mode +
                    " MissionState=" + mission.CurrentState +
                    " RemovedCount=" + removedTypeNames.Count +
                    " RemovedTypes=[" + string.Join(",", removedTypeNames.ToArray()) + "].");
            }
            catch (Exception ex)
            {
                ModLogger.Info("BattleShellSuppressionPatch: campaign-only mission view removal guard failed: " + ex.Message);
            }
        }

        private static bool ShouldSuppressRemoteClientSiegeSingleplayerMissionView(Mission mission)
        {
            if (!GameNetwork.IsClient || GameNetwork.IsServer || mission == null)
                return false;

            if (!MissionMultiplayerCoopSiegeAssaultWithDeploymentMode.HasCoopSiegeRuntimeMarker(mission))
                return false;

            if (!SceneRuntimeClassifier.IsSceneAwareBattleRuntimeScene(mission.SceneName ?? string.Empty))
                return false;

            BattleScenarioContextMessage scenarioContext =
                BattleSnapshotRuntimeState.GetCurrent()?.ScenarioContext ??
                BattleSnapshotRuntimeState.GetState()?.ScenarioContext;
            return ExactCampaignSiegeAssaultWithDeploymentRuntime.IsSiegeAssaultScenario(scenarioContext);
        }

        private static bool ShouldEnsureSiegeNativeOpposingTeamsBeforeLobbyEarlyStart(Mission mission)
        {
            if (!ExperimentalFeatures.EnableSiegeReplayEarlyNativeTeamBootstrap)
                return false;

            if (!GameNetwork.IsServer || mission?.Teams == null)
                return false;

            if (!MissionMultiplayerCoopSiegeAssaultWithDeploymentMode.HasCoopSiegeRuntimeMarker(mission))
                return false;

            string sceneName = mission.SceneName ?? string.Empty;
            return SceneRuntimeClassifier.IsSceneAwareBattleRuntimeScene(sceneName);
        }

        private static string ResolveSiegeTeamCultureId(BattleSideEnum side, string fallback)
        {
            string optionCultureId = null;
            try
            {
                optionCultureId = side == BattleSideEnum.Attacker
                    ? MultiplayerOptions.OptionType.CultureTeam1.GetStrValue()
                    : MultiplayerOptions.OptionType.CultureTeam2.GetStrValue();
            }
            catch
            {
                optionCultureId = null;
            }

            return BattleSnapshotRuntimeState.ResolveSideCultureId(
                side,
                string.IsNullOrWhiteSpace(optionCultureId) ? fallback : optionCultureId);
        }

        private static BasicCultureObject ResolveCulture(string cultureId)
        {
            try
            {
                return string.IsNullOrWhiteSpace(cultureId)
                    ? null
                    : MBObjectManager.Instance?.GetObject<BasicCultureObject>(cultureId);
            }
            catch
            {
                return null;
            }
        }

        private static Banner TryCreateNativeSiegeBanner(BasicCultureObject culture, uint color, uint color2)
        {
            try
            {
                if (culture == null || culture.Banner == null)
                    return null;

                return new Banner(culture.Banner, color, color2);
            }
            catch (Exception ex)
            {
                ModLogger.Info("BattleShellSuppressionPatch: native siege banner creation failed: " + ex.Message);
                return null;
            }
        }

        private static string BuildSiegeTeamDiagnosticsKey(Mission mission, string source)
        {
            return
                (source ?? "unknown") + "|" +
                (mission?.SceneName ?? "unknown") + "|" +
                (mission?.Mode.ToString() ?? "unknown") + "|" +
                (mission?.CurrentState.ToString() ?? "unknown") + "|" +
                GetTeamCount(mission);
        }

        private static string BuildSiegeTeamStateSummary(Mission mission)
        {
            MissionLobbyComponent lobbyComponent = mission?.GetMissionBehavior<MissionLobbyComponent>();
            return
                "Scene=" + (mission?.SceneName ?? "null") +
                " Mode=" + (mission?.Mode.ToString() ?? "null") +
                " MissionState=" + (mission?.CurrentState.ToString() ?? "null") +
                " BattlePhase=" + CoopBattlePhaseRuntimeState.GetPhase() +
                " LobbyState=" + (lobbyComponent?.CurrentMultiplayerState.ToString() ?? "null") +
                " TeamCount=" + GetTeamCount(mission) +
                " HasAttacker=" + (mission?.Teams?.Attacker != null) +
                " HasDefender=" + (mission?.Teams?.Defender != null) +
                " HasSpectator=" + (mission?.SpectatorTeam != null) +
                " NetworkPeers=" + SafeNetworkPeerCount() +
                " MissionBehaviors=" + (mission?.MissionBehaviors?.Count.ToString() ?? "null") +
                " HasLobbyComponent=" + (lobbyComponent != null) +
                " HasTimerComponent=" + (mission?.GetMissionBehavior<MultiplayerTimerComponent>() != null) +
                " HasTeamSelectComponent=" + (mission?.GetMissionBehavior<MultiplayerTeamSelectComponent>() != null) +
                " HasCoopSiegeServer=" + (mission?.GetMissionBehavior<MissionMultiplayerCoopSiegeAssaultWithDeployment>() != null) +
                " HasCoopSiegeClient=" + (mission?.GetMissionBehavior<MissionMultiplayerCoopSiegeAssaultWithDeploymentClient>() != null) + ".";
        }

        private static int GetTeamCount(Mission mission)
        {
            try
            {
                return mission?.Teams?.Count ?? -1;
            }
            catch
            {
                return -1;
            }
        }

        private static int SafeNetworkPeerCount()
        {
            try
            {
                return GameNetwork.NetworkPeers?.Count ?? -1;
            }
            catch
            {
                return -1;
            }
        }

        private static bool ShouldLogMissionStartupObservation(Mission mission)
        {
            if (mission == null)
                return false;

            if (!GameNetwork.IsServer && !GameNetwork.IsClient)
                return false;

            string sceneName = mission.SceneName ?? string.Empty;
            if (!SceneRuntimeClassifier.IsSceneAwareBattleRuntimeScene(sceneName))
                return false;

            return MissionMultiplayerCoopSiegeAssaultWithDeploymentMode.HasCoopSiegeRuntimeMarker(mission);
        }

        private static void LogMissionStateLifecycleObservation(Mission mission, string source, string extra = null)
        {
            try
            {
                if (!ShouldLogMissionStartupObservation(mission))
                    return;

                string sceneName = mission.SceneName ?? string.Empty;
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
                Mission mission = Mission.Current;
                if (!ShouldLogMissionStartupObservation(mission))
                    return;

                string sceneName = mission.SceneName ?? string.Empty;
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
                if (!ShouldLogMissionStartupObservation(mission))
                    return;

                string sceneName = mission.SceneName ?? string.Empty;
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
                if (missionStateInstance == null)
                    return;

                Type missionStateType = missionStateInstance.GetType();
                Mission mission = missionStateType
                    .GetProperty("CurrentMission", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    ?.GetValue(missionStateInstance) as Mission;
                if (!ShouldLogMissionStartupObservation(mission))
                    return;

                string sceneName = mission.SceneName ?? string.Empty;
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
                if (!ShouldLogMissionStartupObservation(mission))
                    return;

                string sceneName = mission.SceneName ?? string.Empty;
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
            if (!EnableDedicatedMissionLoadBypass)
                return false;

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
            if (!EnableDedicatedMissionLoadBypass)
                return false;

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
                if (!ShouldLogMissionStartupObservation(mission))
                    return;

                string sceneName = mission.SceneName ?? string.Empty;
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
                if (missionStateInstance == null)
                    return;

                Mission mission = missionStateInstance.GetType()
                    .GetProperty("CurrentMission", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    ?.GetValue(missionStateInstance) as Mission;
                if (!ShouldLogMissionStartupObservation(mission))
                    return;

                string sceneName = mission.SceneName ?? string.Empty;
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
                if (!EnableDedicatedMissionLoadBypass)
                    return false;

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
                if (!EnableDedicatedMissionLoadBypass)
                    return false;

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
                if (!EnableDedicatedMissionLoadBypass)
                    return;

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
                if (!ShouldLogMissionStartupObservation(mission))
                    return;

                if (IsDedicatedServerProcess() &&
                    TryGetMissionState(mission, out Mission.State missionState) &&
                    missionState != Mission.State.NewlyCreated &&
                    missionState != Mission.State.Initializing)
                {
                    return;
                }

                string sceneName = mission.SceneName ?? string.Empty;
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
