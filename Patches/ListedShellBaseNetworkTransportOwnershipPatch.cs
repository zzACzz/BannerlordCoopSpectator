using System;
using System.Reflection;
using CoopSpectator.Infrastructure;
using HarmonyLib;
using NetworkMessages.FromServer;
using TaleWorlds.MountAndBlade;

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

        private static bool HandleNewClientConnect_Prefix(object __instance, PlayerConnectionInfo playerConnectionInfo)
        {
            if (!GameNetwork.IsServer)
                return true;

            NetworkCommunicator targetPeer = playerConnectionInfo?.NetworkPeer;
            if (targetPeer == null || targetPeer.IsServerPeer)
                return true;

            Mission mission = Mission.Current;
            if (!ShouldOwnListedShellInitializeCustomGameIngress(mission))
                return true;

            return !ListedShellNetworkBootstrapRuntime.TrySendListedNewClientBootstrap(mission, targetPeer);
        }

        private static bool HandleServerEventInitializeCustomGame_Prefix(object __instance, object baseMessage)
        {
            if (!GameNetwork.IsClient)
                return true;

            InitializeCustomGameMessage message = baseMessage as InitializeCustomGameMessage;
            if (message == null || !ListedShellNetworkBootstrapRuntime.ShouldOwnInitializeCustomGameReceive(message))
                return true;

            _ = ListedShellNetworkBootstrapRuntime.HandleListedInitializeCustomGameReceiveAsync(message);
            ModLogger.Info(
                "ListedShellBaseNetworkTransportOwnershipPatch: intercepted listed InitializeCustomGameMessage and bypassed native custom/community client state gate. " +
                "InMission=" + message.InMission +
                " GameType=" + (message.GameType ?? string.Empty) +
                " Map=" + (message.Map ?? string.Empty) +
                " BattleIndex=" + message.BattleIndex + ".");
            return false;
        }

        private static bool HandleServerEventLoadMission_Prefix(object __instance, object baseMessage)
        {
            if (!GameNetwork.IsClient)
                return true;

            LoadMission message = baseMessage as LoadMission;
            if (message == null || !ListedShellNetworkBootstrapRuntime.ShouldOwnLoadMissionReceive(message))
                return true;

            _ = ListedShellNetworkBootstrapRuntime.HandleListedLoadMissionReceiveAsync(__instance, message);
            ModLogger.Info(
                "ListedShellBaseNetworkTransportOwnershipPatch: intercepted listed LoadMission and bypassed native BaseNetworkComponent mission-open path. " +
                "GameType=" + (message.GameType ?? string.Empty) +
                " Map=" + (message.Map ?? string.Empty) +
                " BattleIndex=" + message.BattleIndex + ".");
            return false;
        }

        private static bool HandleServerEventUnloadMission_Prefix(object __instance, object baseMessage)
        {
            if (!GameNetwork.IsClient)
                return true;

            UnloadMission message = baseMessage as UnloadMission;
            if (message == null || !ListedShellNetworkBootstrapRuntime.ShouldOwnUnloadMissionReceive())
                return true;

            _ = ListedShellNetworkBootstrapRuntime.HandleListedUnloadMissionReceiveAsync(__instance, message);
            ModLogger.Info(
                "ListedShellBaseNetworkTransportOwnershipPatch: intercepted listed UnloadMission and bypassed native BaseNetworkComponent unload coroutine. " +
                "UnloadingForBattleIndexMismatch=" + message.UnloadingForBattleIndexMismatch + ".");
            return false;
        }

        private static bool ShouldOwnListedShellInitializeCustomGameIngress(Mission mission)
        {
            if (mission == null)
                return false;

            return mission.GetMissionBehavior<ListedShellCompatibilityMode>() != null ||
                mission.GetMissionBehavior<ListedShellCompatibilityModeClient>() != null;
        }
    }
}
