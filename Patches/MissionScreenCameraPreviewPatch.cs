using System;
using System.Reflection;
using CoopSpectator.GameMode;
using CoopSpectator.Infrastructure;
using CoopSpectator.UI;
using HarmonyLib;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.View.Screens;

namespace CoopSpectator.Patches
{
    /// <summary>
    /// Makes the battle selection camera preview win over vanilla spawned-agent-visual spectator routing
    /// without propagating local preview follow switches over the network.
    /// </summary>
    public static class MissionScreenCameraPreviewPatch
    {
        private static readonly FieldInfo FollowedAgentField =
            typeof(MissionPeer).GetField("_followedAgent", BindingFlags.Instance | BindingFlags.NonPublic);

        private static string _lastSuppressedSpawnVisualKey;
        private static string _lastTemporarilyRemappedMissionTypeKey;
        private static string _lastSuppressedFollowEchoKey;

        private struct CameraPreviewSpectatingState
        {
            public MissionPeer LocalMissionPeer;
            public bool HadSpawnedAgentVisuals;
            public bool AppliedSpawnVisualSuppression;
            public MissionLobbyComponent MissionLobbyComponent;
            public MultiplayerGameType OriginalMissionType;
            public bool AppliedMissionTypeRemap;
            public int PreviewAgentIndex;
        }

        public static void Apply(Harmony harmony)
        {
            PatchMissionScreenGetSpectatingData(harmony);
            PatchMissionScreenFindNextCameraAttachableAgent(harmony);
            PatchMissionPeerFollowedAgent(harmony);
        }

        private static void PatchMissionScreenGetSpectatingData(Harmony harmony)
        {
            MethodInfo target = typeof(MissionScreen).GetMethod(
                "GetSpectatingData",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            MethodInfo prefix = typeof(MissionScreenCameraPreviewPatch).GetMethod(
                nameof(MissionScreen_GetSpectatingData_Prefix),
                BindingFlags.Static | BindingFlags.NonPublic);
            MethodInfo postfix = typeof(MissionScreenCameraPreviewPatch).GetMethod(
                nameof(MissionScreen_GetSpectatingData_Postfix),
                BindingFlags.Static | BindingFlags.NonPublic);
            if (target == null || prefix == null || postfix == null)
            {
                ModLogger.Info("MissionScreenCameraPreviewPatch: MissionScreen.GetSpectatingData not found. Skip.");
                return;
            }

            harmony.Patch(target, prefix: new HarmonyMethod(prefix), postfix: new HarmonyMethod(postfix));
            ModLogger.Info("MissionScreenCameraPreviewPatch: prefix/postfix applied to MissionScreen.GetSpectatingData.");
        }

        private static void PatchMissionScreenFindNextCameraAttachableAgent(Harmony harmony)
        {
            MethodInfo target = null;
            foreach (MethodInfo candidate in typeof(MissionScreen).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (!string.Equals(candidate.Name, "FindNextCameraAttachableAgent", StringComparison.Ordinal))
                    continue;

                ParameterInfo[] parameters = candidate.GetParameters();
                if (parameters.Length == 4 &&
                    parameters[0].ParameterType == typeof(Agent) &&
                    parameters[2].ParameterType == typeof(int) &&
                    parameters[3].ParameterType == typeof(Vec3))
                {
                    target = candidate;
                    break;
                }
            }

            MethodInfo prefix = typeof(MissionScreenCameraPreviewPatch).GetMethod(
                nameof(MissionScreen_FindNextCameraAttachableAgent_Prefix),
                BindingFlags.Static | BindingFlags.NonPublic);
            if (target == null || prefix == null)
            {
                ModLogger.Info("MissionScreenCameraPreviewPatch: MissionScreen.FindNextCameraAttachableAgent not found. Skip.");
                return;
            }

            harmony.Patch(target, prefix: new HarmonyMethod(prefix));
            ModLogger.Info("MissionScreenCameraPreviewPatch: prefix applied to MissionScreen.FindNextCameraAttachableAgent.");
        }

        private static void PatchMissionPeerFollowedAgent(Harmony harmony)
        {
            MethodInfo target = typeof(MissionPeer).GetProperty(
                "FollowedAgent",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?.GetSetMethod(true);
            MethodInfo prefix = typeof(MissionScreenCameraPreviewPatch).GetMethod(
                nameof(MissionPeer_FollowedAgent_Prefix),
                BindingFlags.Static | BindingFlags.NonPublic);
            if (target == null || prefix == null || FollowedAgentField == null)
            {
                ModLogger.Info("MissionScreenCameraPreviewPatch: MissionPeer.FollowedAgent setter not found. Skip.");
                return;
            }

            harmony.Patch(target, prefix: new HarmonyMethod(prefix));
            ModLogger.Info("MissionScreenCameraPreviewPatch: prefix applied to MissionPeer.FollowedAgent.");
        }

        private static void MissionScreen_GetSpectatingData_Prefix(
            MissionScreen __instance,
            ref CameraPreviewSpectatingState __state)
        {
            __state = default;

            try
            {
                if (__instance?.Mission == null ||
                    !GameNetwork.IsClient ||
                    !GameNetwork.IsSessionActive ||
                    !MissionMultiplayerCoopBattleMode.IsBattleMapSceneName(__instance.Mission.SceneName))
                {
                    return;
                }

                if (!CoopMissionSelectionView.TryGetActiveCameraFollowAgent(out Agent previewAgent))
                    return;

                MissionPeer localMissionPeer = GameNetwork.MyPeer?.GetComponent<MissionPeer>();
                if (localMissionPeer == null ||
                    previewAgent == null ||
                    !previewAgent.IsActive() ||
                    !ReferenceEquals(previewAgent.Mission, __instance.Mission))
                {
                    return;
                }

                __state.LocalMissionPeer = localMissionPeer;
                __state.HadSpawnedAgentVisuals = localMissionPeer.HasSpawnedAgentVisuals;
                __state.PreviewAgentIndex = previewAgent.Index;
                MissionLobbyComponent missionLobbyComponent = __instance.Mission.GetMissionBehavior<MissionLobbyComponent>();
                if (missionLobbyComponent != null && missionLobbyComponent.MissionType == MultiplayerGameType.Battle)
                {
                    __state.MissionLobbyComponent = missionLobbyComponent;
                    __state.OriginalMissionType = missionLobbyComponent.MissionType;
                    missionLobbyComponent.MissionType = MultiplayerGameType.TeamDeathmatch;
                    __state.AppliedMissionTypeRemap = true;

                    string missionTypeLogKey = previewAgent.Index + "|" + (__instance.Mission.SceneName ?? "null");
                    if (!string.Equals(_lastTemporarilyRemappedMissionTypeKey, missionTypeLogKey, StringComparison.Ordinal))
                    {
                        _lastTemporarilyRemappedMissionTypeKey = missionTypeLogKey;
                        ModLogger.Info(
                            "MissionScreenCameraPreviewPatch: temporarily remapped MissionLobbyComponent.MissionType from Battle to TeamDeathmatch for camera preview. " +
                            "PreviewAgentIndex=" + previewAgent.Index +
                            " Mission=" + (__instance.Mission.SceneName ?? "null"));
                    }
                }

                if (!localMissionPeer.HasSpawnedAgentVisuals)
                    return;

                localMissionPeer.HasSpawnedAgentVisuals = false;
                __state.AppliedSpawnVisualSuppression = true;

                string logKey = previewAgent.Index + "|" + (__instance.Mission.SceneName ?? "null");
                if (!string.Equals(_lastSuppressedSpawnVisualKey, logKey, StringComparison.Ordinal))
                {
                    _lastSuppressedSpawnVisualKey = logKey;
                    ModLogger.Info(
                        "MissionScreenCameraPreviewPatch: temporarily suppressed local HasSpawnedAgentVisuals for camera preview. " +
                        "PreviewAgentIndex=" + previewAgent.Index +
                        " Mission=" + (__instance.Mission.SceneName ?? "null"));
                }
            }
            catch (Exception ex)
            {
                ModLogger.Info("MissionScreenCameraPreviewPatch: GetSpectatingData prefix failed open: " + ex.Message);
            }
        }

        private static void MissionScreen_GetSpectatingData_Postfix(CameraPreviewSpectatingState __state)
        {
            try
            {
                if (__state.AppliedSpawnVisualSuppression && __state.LocalMissionPeer != null)
                    __state.LocalMissionPeer.HasSpawnedAgentVisuals = __state.HadSpawnedAgentVisuals;

                if (__state.AppliedMissionTypeRemap && __state.MissionLobbyComponent != null)
                    __state.MissionLobbyComponent.MissionType = __state.OriginalMissionType;
            }
            catch (Exception ex)
            {
                ModLogger.Info("MissionScreenCameraPreviewPatch: GetSpectatingData postfix failed open: " + ex.Message);
            }
        }

        private static bool MissionScreen_FindNextCameraAttachableAgent_Prefix(
            MissionScreen __instance,
            Agent currentAgent,
            ref Agent __result)
        {
            try
            {
                if (__instance?.Mission == null ||
                    !GameNetwork.IsClient ||
                    !GameNetwork.IsSessionActive ||
                    !MissionMultiplayerCoopBattleMode.IsBattleMapSceneName(__instance.Mission.SceneName) ||
                    (!CoopMissionSelectionView.IsCommanderDeploymentPlacementInputActive() &&
                     !CoopMissionSelectionView.IsAiControlObservationActive()))
                {
                    return true;
                }

                __result = currentAgent;
                return false;
            }
            catch
            {
                return true;
            }
        }

        private static bool MissionPeer_FollowedAgent_Prefix(MissionPeer __instance, Agent value)
        {
            try
            {
                if (!CoopMissionSelectionView.ShouldSuppressLocalPreviewFollowedAgentEcho(__instance, value))
                    return true;

                FollowedAgentField.SetValue(__instance, value);
                string followedAgentIndex = value?.Index.ToString() ?? "null";
                string logKey = (__instance.Peer?.Index.ToString() ?? "null") + "|" + followedAgentIndex;
                if (!string.Equals(_lastSuppressedFollowEchoKey, logKey, StringComparison.Ordinal))
                {
                    _lastSuppressedFollowEchoKey = logKey;
                    ModLogger.Info(
                        "MissionScreenCameraPreviewPatch: suppressed local MissionPeer.FollowedAgent network echo for camera preview. " +
                        "Peer=" + (__instance.Peer?.UserName ?? __instance.Peer?.Index.ToString() ?? "null") +
                        " FollowedAgentIndex=" + followedAgentIndex);
                }

                return false;
            }
            catch (Exception ex)
            {
                ModLogger.Info("MissionScreenCameraPreviewPatch: MissionPeer.FollowedAgent prefix failed open: " + ex.Message);
                return true;
            }
        }
    }
}
