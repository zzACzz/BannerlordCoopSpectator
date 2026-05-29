using System;
using System.Reflection;
using System.Threading.Tasks;
using CoopSpectator.Infrastructure;
using HarmonyLib;
using NetworkMessages.FromClient;
using NetworkMessages.FromServer;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.Multiplayer.NetworkComponents;
using TaleWorlds.MountAndBlade.Network.Messages;

namespace CoopSpectator.Patches
{
    public static class FinishedLoadingMissionReadyGatePatch
    {
        private static FieldInfo _baseNetworkComponentDataField;
        private static MethodInfo _ensureBaseNetworkComponentDataMethod;

        public static void Apply(Harmony harmony)
        {
            try
            {
                Type targetType = AccessTools.TypeByName("TaleWorlds.MountAndBlade.Multiplayer.NetworkComponents.BaseNetworkComponent");
                if (targetType == null)
                {
                    ModLogger.Info("FinishedLoadingMissionReadyGatePatch: BaseNetworkComponent type not found. Skip.");
                    return;
                }

                MethodInfo targetMethod = targetType.GetMethod(
                    "HandleClientEventFinishedLoading",
                    BindingFlags.Instance | BindingFlags.NonPublic,
                    null,
                    new[] { typeof(NetworkCommunicator), typeof(GameNetworkMessage) },
                    null);
                MethodInfo prefixMethod = typeof(FinishedLoadingMissionReadyGatePatch).GetMethod(
                    nameof(HandleClientEventFinishedLoading_Prefix),
                    BindingFlags.Static | BindingFlags.NonPublic);
                if (targetMethod == null || prefixMethod == null)
                {
                    ModLogger.Info("FinishedLoadingMissionReadyGatePatch: target/prefix method not found. Skip.");
                    return;
                }

                _baseNetworkComponentDataField = targetType.GetField("_baseNetworkComponentData", BindingFlags.Instance | BindingFlags.NonPublic);
                _ensureBaseNetworkComponentDataMethod = targetType.GetMethod("EnsureBaseNetworkComponentData", BindingFlags.Instance | BindingFlags.NonPublic);

                harmony.Patch(targetMethod, prefix: new HarmonyMethod(prefixMethod));
                ModLogger.Info("FinishedLoadingMissionReadyGatePatch: patched BaseNetworkComponent.HandleClientEventFinishedLoading.");
            }
            catch (Exception ex)
            {
                ModLogger.Error("FinishedLoadingMissionReadyGatePatch.Apply failed.", ex);
            }
        }

        private static bool HandleClientEventFinishedLoading_Prefix(
            object __instance,
            NetworkCommunicator networkPeer,
            GameNetworkMessage baseMessage,
            ref bool __result)
        {
            if (!GameNetwork.IsServer)
                return true;

            if (!(baseMessage is FinishedLoading message))
                return true;

            if (networkPeer == null || networkPeer.IsServerPeer)
                return true;

            Mission currentMission = Mission.Current;
            if (ListedShellSessionTransportRuntime.ShouldOwnListedServerFinishedLoadingValidation(currentMission))
            {
                ListedShellSessionTransportRuntime.HandleListedServerFinishedLoadingValidation(
                    networkPeer,
                    message,
                    "FinishedLoadingMissionReadyGatePatch");
                __result = true;
                return false;
            }

            if (!PendingBattleMissionStartupState.ShouldDelayServerFinishedLoadingValidation(currentMission, out string delayDetails))
                return true;

            HandleClientEventFinishedLoadingDeferred(__instance, networkPeer, message, delayDetails);
            __result = true;
            return false;
        }

        private static async void HandleClientEventFinishedLoadingDeferred(
            object instance,
            NetworkCommunicator networkPeer,
            FinishedLoading message,
            string initialDelayDetails)
        {
            DateTime startedUtc = DateTime.UtcNow;
            string finalDelayDetails = initialDelayDetails ?? string.Empty;

            try
            {
                EnsureBaseNetworkComponentData(instance);
                while (PendingBattleMissionStartupState.ShouldDelayServerFinishedLoadingValidation(Mission.Current, out string delayDetails))
                {
                    finalDelayDetails = delayDetails ?? string.Empty;
                    await Task.Delay(1);
                }

                if (networkPeer == null || networkPeer.IsServerPeer)
                    return;

                int currentBattleIndex = GetCurrentBattleIndex(instance);
                Mission currentMission = Mission.Current;
                bool shouldUnload = currentMission == null || currentBattleIndex != message.BattleIndex;

                Debug.Print("Server: " + networkPeer.UserName + " has finished loading. From now on, I will include him in the broadcasted messages");

                if (shouldUnload)
                {
                    GameNetwork.BeginModuleEventAsServer(networkPeer);
                    GameNetwork.WriteMessage(new UnloadMission(true));
                    GameNetwork.EndModuleEventAsServer();
                }
                else
                {
                    GameNetwork.ClientFinishedLoading(networkPeer);
                }

                ModLogger.Info(
                    "FinishedLoadingMissionReadyGatePatch: processed deferred FinishedLoading validation. " +
                    "Peer=" + (networkPeer.UserName ?? "unknown") +
                    " DeferredForMs=" + (DateTime.UtcNow - startedUtc).TotalMilliseconds.ToString("0") +
                    " InitialDelayDetails=" + (initialDelayDetails ?? string.Empty) +
                    " FinalDelayDetails=" + (finalDelayDetails ?? string.Empty) +
                    " MissionScene=" + (currentMission?.SceneName ?? "null") +
                    " MissionState=" + (currentMission?.CurrentState.ToString() ?? "null") +
                    " CurrentBattleIndex=" + currentBattleIndex +
                    " FinishedLoadingBattleIndex=" + message.BattleIndex +
                    " Action=" + (shouldUnload ? "UnloadMission" : "ClientFinishedLoading") + ".");
            }
            catch (Exception ex)
            {
                ModLogger.Error(
                    "FinishedLoadingMissionReadyGatePatch: deferred FinishedLoading handling failed. " +
                    "Peer=" + (networkPeer?.UserName ?? "unknown") +
                    " InitialDelayDetails=" + (initialDelayDetails ?? string.Empty) +
                    " FinalDelayDetails=" + (finalDelayDetails ?? string.Empty) + ".",
                    ex);
            }
        }

        private static void EnsureBaseNetworkComponentData(object instance)
        {
            try
            {
                _ensureBaseNetworkComponentDataMethod?.Invoke(instance, Array.Empty<object>());
            }
            catch (Exception ex)
            {
                ModLogger.Info("FinishedLoadingMissionReadyGatePatch: EnsureBaseNetworkComponentData invoke failed: " + ex.Message);
            }
        }

        private static int GetCurrentBattleIndex(object instance)
        {
            try
            {
                BaseNetworkComponentData data = _baseNetworkComponentDataField?.GetValue(instance) as BaseNetworkComponentData;
                return data?.CurrentBattleIndex ?? -1;
            }
            catch (Exception ex)
            {
                ModLogger.Info("FinishedLoadingMissionReadyGatePatch: failed to read CurrentBattleIndex: " + ex.Message);
                return -1;
            }
        }
    }
}
