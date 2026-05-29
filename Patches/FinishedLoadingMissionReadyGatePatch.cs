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

                PendingBattleFinishedLoadingTransportRuntime.InitializeBaseNetworkContracts(targetType);

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

            if (!PendingBattleFinishedLoadingTransportRuntime.ShouldOwnDeferredServerFinishedLoadingValidation(currentMission, out string delayDetails))
                return true;

            PendingBattleFinishedLoadingTransportRuntime.HandleDeferredServerFinishedLoadingValidation(
                __instance,
                networkPeer,
                message,
                delayDetails,
                "FinishedLoadingMissionReadyGatePatch");
            __result = true;
            return false;
        }
    }
}
