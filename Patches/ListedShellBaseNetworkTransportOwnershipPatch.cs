using System;
using System.Reflection;
using CoopSpectator.Infrastructure;
using HarmonyLib;
using NetworkMessages.FromClient;
using NetworkMessages.FromServer;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.Network.Messages;

namespace CoopSpectator.Patches
{
    /// <summary>
    /// Owns the explicit listed BaseNetworkComponent transport graph so send/receive bootstrap
    /// interception is routed through one patch surface instead of separate listed message patches.
    /// </summary>
    internal static class ListedShellBaseNetworkTransportOwnershipPatch
    {
        private static bool _isApplied;

        public static void Apply(Harmony harmony)
        {
            if (_isApplied)
                return;

            try
            {
                Type targetType = AccessTools.TypeByName("TaleWorlds.MountAndBlade.Multiplayer.NetworkComponents.BaseNetworkComponent");
                if (targetType == null)
                {
                    ModLogger.Info("ListedShellBaseNetworkTransportOwnershipPatch: BaseNetworkComponent type not found. Skip.");
                    return;
                }

                ListedShellNetworkBootstrapRuntime.InitializeBaseNetworkContracts(targetType);
                PatchHandleNewClientConnect(harmony, targetType);
                PatchHandleServerEventInitializeCustomGame(harmony, targetType);
                PatchHandleServerEventLoadMission(harmony, targetType);
                PatchHandleServerEventUnloadMission(harmony, targetType);
                PatchHandleClientEventFinishedLoading(harmony, targetType);

                _isApplied = true;
                ModLogger.Info("ListedShellBaseNetworkTransportOwnershipPatch: patched listed BaseNetworkComponent transport graph.");
            }
            catch (Exception ex)
            {
                ModLogger.Error("ListedShellBaseNetworkTransportOwnershipPatch.Apply failed.", ex);
            }
        }

        private static void PatchHandleNewClientConnect(Harmony harmony, Type targetType)
        {
            MethodInfo targetMethod = targetType.GetMethod(
                "HandleNewClientConnect",
                BindingFlags.Instance | BindingFlags.Public,
                null,
                new[] { typeof(PlayerConnectionInfo) },
                null);
            MethodInfo prefixMethod = typeof(ListedShellBaseNetworkTransportOwnershipPatch).GetMethod(
                nameof(HandleNewClientConnect_Prefix),
                BindingFlags.Static | BindingFlags.NonPublic);
            if (targetMethod == null || prefixMethod == null)
            {
                ModLogger.Info("ListedShellBaseNetworkTransportOwnershipPatch: HandleNewClientConnect target/prefix not found. Skip.");
                return;
            }

            harmony.Patch(targetMethod, prefix: new HarmonyMethod(prefixMethod));
        }

        private static void PatchHandleServerEventInitializeCustomGame(Harmony harmony, Type targetType)
        {
            MethodInfo targetMethod = targetType.GetMethod(
                "HandleServerEventInitializeCustomGame",
                BindingFlags.Instance | BindingFlags.NonPublic,
                null,
                new[] { typeof(TaleWorlds.MountAndBlade.Network.Messages.GameNetworkMessage) },
                null);
            MethodInfo prefixMethod = typeof(ListedShellBaseNetworkTransportOwnershipPatch).GetMethod(
                nameof(HandleServerEventInitializeCustomGame_Prefix),
                BindingFlags.Static | BindingFlags.NonPublic);
            if (targetMethod == null || prefixMethod == null)
            {
                ModLogger.Info("ListedShellBaseNetworkTransportOwnershipPatch: HandleServerEventInitializeCustomGame target/prefix not found. Skip.");
                return;
            }

            harmony.Patch(targetMethod, prefix: new HarmonyMethod(prefixMethod));
        }

        private static void PatchHandleServerEventLoadMission(Harmony harmony, Type targetType)
        {
            MethodInfo targetMethod = targetType.GetMethod(
                "HandleServerEventLoadMission",
                BindingFlags.Instance | BindingFlags.NonPublic,
                null,
                new[] { typeof(TaleWorlds.MountAndBlade.Network.Messages.GameNetworkMessage) },
                null);
            MethodInfo prefixMethod = typeof(ListedShellBaseNetworkTransportOwnershipPatch).GetMethod(
                nameof(HandleServerEventLoadMission_Prefix),
                BindingFlags.Static | BindingFlags.NonPublic);
            if (targetMethod == null || prefixMethod == null)
            {
                ModLogger.Info("ListedShellBaseNetworkTransportOwnershipPatch: HandleServerEventLoadMission target/prefix not found. Skip.");
                return;
            }

            harmony.Patch(targetMethod, prefix: new HarmonyMethod(prefixMethod));
        }

        private static void PatchHandleServerEventUnloadMission(Harmony harmony, Type targetType)
        {
            MethodInfo targetMethod = targetType.GetMethod(
                "HandleServerEventUnloadMission",
                BindingFlags.Instance | BindingFlags.NonPublic,
                null,
                new[] { typeof(TaleWorlds.MountAndBlade.Network.Messages.GameNetworkMessage) },
                null);
            MethodInfo prefixMethod = typeof(ListedShellBaseNetworkTransportOwnershipPatch).GetMethod(
                nameof(HandleServerEventUnloadMission_Prefix),
                BindingFlags.Static | BindingFlags.NonPublic);
            if (targetMethod == null || prefixMethod == null)
            {
                ModLogger.Info("ListedShellBaseNetworkTransportOwnershipPatch: HandleServerEventUnloadMission target/prefix not found. Skip.");
                return;
            }

            harmony.Patch(targetMethod, prefix: new HarmonyMethod(prefixMethod));
        }

        private static void PatchHandleClientEventFinishedLoading(Harmony harmony, Type targetType)
        {
            MethodInfo targetMethod = targetType.GetMethod(
                "HandleClientEventFinishedLoading",
                BindingFlags.Instance | BindingFlags.NonPublic,
                null,
                new[] { typeof(NetworkCommunicator), typeof(GameNetworkMessage) },
                null);
            MethodInfo prefixMethod = typeof(ListedShellBaseNetworkTransportOwnershipPatch).GetMethod(
                nameof(HandleClientEventFinishedLoading_Prefix),
                BindingFlags.Static | BindingFlags.NonPublic);
            if (targetMethod == null || prefixMethod == null)
            {
                ModLogger.Info("ListedShellBaseNetworkTransportOwnershipPatch: HandleClientEventFinishedLoading target/prefix not found. Skip.");
                return;
            }

            harmony.Patch(targetMethod, prefix: new HarmonyMethod(prefixMethod));
        }

        private static bool HandleNewClientConnect_Prefix(object __instance, PlayerConnectionInfo playerConnectionInfo)
        {
            return ListedShellNetworkBootstrapRuntime.TryHandleListedNewClientConnect(
                Mission.Current,
                playerConnectionInfo,
                "ListedShellBaseNetworkTransportOwnershipPatch");
        }

        private static bool HandleServerEventInitializeCustomGame_Prefix(object __instance, object baseMessage)
        {
            return ListedShellNetworkBootstrapRuntime.TryHandleInitializeCustomGameReceive(
                baseMessage,
                "ListedShellBaseNetworkTransportOwnershipPatch");
        }

        private static bool HandleServerEventLoadMission_Prefix(object __instance, object baseMessage)
        {
            return ListedShellNetworkBootstrapRuntime.TryHandleLoadMissionReceive(
                __instance,
                baseMessage,
                "ListedShellBaseNetworkTransportOwnershipPatch");
        }

        private static bool HandleServerEventUnloadMission_Prefix(object __instance, object baseMessage)
        {
            return ListedShellNetworkBootstrapRuntime.TryHandleUnloadMissionReceive(
                __instance,
                baseMessage,
                "ListedShellBaseNetworkTransportOwnershipPatch");
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
                    "ListedShellBaseNetworkTransportOwnershipPatch");
                __result = true;
                return false;
            }

            if (!PendingBattleFinishedLoadingTransportRuntime.ShouldOwnDeferredServerFinishedLoadingValidation(currentMission, out string delayDetails))
                return true;

            PendingBattleFinishedLoadingTransportRuntime.HandleDeferredServerFinishedLoadingValidation(
                networkPeer,
                message,
                delayDetails,
                "ListedShellBaseNetworkTransportOwnershipPatch");
            __result = true;
            return false;
        }
    }
}
