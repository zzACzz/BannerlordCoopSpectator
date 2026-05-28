using System;
using System.Reflection;
using CoopSpectator.Infrastructure;
using CoopSpectator.UI;
using HarmonyLib;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.View.Screens;

namespace CoopSpectator.Patches
{
    /// <summary>
    /// Makes the battle selection camera preview win over vanilla spectator routing
    /// by overriding the resolved spectating result instead of mutating mission state.
    /// </summary>
    public static class MissionScreenCameraPreviewPatch
    {
        private static string _lastPreviewOverrideKey;

        public static void Apply(Harmony harmony)
        {
            PatchMissionScreenGetSpectatingData(harmony);
        }

        private static void PatchMissionScreenGetSpectatingData(Harmony harmony)
        {
            MethodInfo target = typeof(MissionScreen).GetMethod(
                "GetSpectatingData",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            MethodInfo postfix = typeof(MissionScreenCameraPreviewPatch).GetMethod(
                nameof(MissionScreen_GetSpectatingData_Postfix),
                BindingFlags.Static | BindingFlags.NonPublic);
            if (target == null || postfix == null)
            {
                ModLogger.Info("MissionScreenCameraPreviewPatch: MissionScreen.GetSpectatingData not found. Skip.");
                return;
            }

            harmony.Patch(target, postfix: new HarmonyMethod(postfix));
            ModLogger.Info("MissionScreenCameraPreviewPatch: postfix applied to MissionScreen.GetSpectatingData.");
        }

        private static void MissionScreen_GetSpectatingData_Postfix(
            MissionScreen __instance,
            ref Mission.SpectatorData __result)
        {
            try
            {
                if (__instance?.Mission == null ||
                    !GameNetwork.IsClient ||
                    !GameNetwork.IsSessionActive ||
                    !CoopMissionSelectionView.TryGetActiveCameraPreviewAgent(out Agent previewAgent))
                {
                    return;
                }

                MissionPeer localMissionPeer = GameNetwork.MyPeer?.GetComponent<MissionPeer>();
                if (localMissionPeer == null ||
                    previewAgent == null ||
                    !previewAgent.IsActive() ||
                    !ReferenceEquals(previewAgent.Mission, __instance.Mission))
                {
                    return;
                }

                Agent currentAgentToFollow = __result.AgentToFollow;
                SpectatorCameraTypes previousCameraType = __result.CameraType;
                if (currentAgentToFollow != null &&
                    currentAgentToFollow.Index == previewAgent.Index &&
                    (int)previousCameraType == 2)
                {
                    return;
                }

                MissionLobbyComponent missionLobbyComponent = __instance.Mission.GetMissionBehavior<MissionLobbyComponent>();
                bool missionTypeBlockedPreview =
                    missionLobbyComponent != null &&
                    missionLobbyComponent.MissionType == MultiplayerGameType.Battle;
                bool nativeVisualStateBlockedPreview = localMissionPeer.HasSpawnedAgentVisuals;

                __result = new Mission.SpectatorData(
                    previewAgent,
                    null,
                    (SpectatorCameraTypes)2);

                string logKey =
                    previewAgent.Index + "|" +
                    (__instance.Mission.SceneName ?? "null") + "|" +
                    missionTypeBlockedPreview + "|" +
                    nativeVisualStateBlockedPreview;
                if (!string.Equals(_lastPreviewOverrideKey, logKey, StringComparison.Ordinal))
                {
                    _lastPreviewOverrideKey = logKey;
                    ModLogger.Info(
                        "MissionScreenCameraPreviewPatch: overrode native spectating result for camera preview without mutating mission state. " +
                        "PreviewAgentIndex=" + previewAgent.Index +
                        " PreviousAgentIndex=" + (currentAgentToFollow?.Index.ToString() ?? "null") +
                        " PreviousCameraType=" + (int)previousCameraType +
                        " MissionTypeBlockedPreview=" + missionTypeBlockedPreview +
                        " NativeVisualStateBlockedPreview=" + nativeVisualStateBlockedPreview +
                        " Mission=" + (__instance.Mission.SceneName ?? "null"));
                }
            }
            catch (Exception ex)
            {
                ModLogger.Info("MissionScreenCameraPreviewPatch: GetSpectatingData postfix failed open: " + ex.Message);
            }
        }
    }
}
