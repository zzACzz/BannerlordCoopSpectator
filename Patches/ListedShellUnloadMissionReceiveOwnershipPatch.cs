using System;
using System.Reflection;
using System.Threading.Tasks;
using CoopSpectator.GameMode;
using CoopSpectator.Infrastructure;
using HarmonyLib;
using NetworkMessages.FromServer;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.Patches
{
    /// <summary>
    /// Owns the listed-shell UnloadMission receive path so explicit listed teardown no longer depends
    /// on BaseNetworkComponent.HandleServerEventUnloadMissionAux.
    /// </summary>
    internal static class ListedShellUnloadMissionReceiveOwnershipPatch
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
                    ModLogger.Info("ListedShellUnloadMissionReceiveOwnershipPatch: BaseNetworkComponent type not found. Skip.");
                    return;
                }

                MethodInfo targetMethod = targetType.GetMethod(
                    "HandleServerEventUnloadMission",
                    BindingFlags.Instance | BindingFlags.NonPublic,
                    null,
                    new[] { typeof(TaleWorlds.MountAndBlade.Network.Messages.GameNetworkMessage) },
                    null);
                MethodInfo prefixMethod = typeof(ListedShellUnloadMissionReceiveOwnershipPatch).GetMethod(
                    nameof(HandleServerEventUnloadMission_Prefix),
                    BindingFlags.Static | BindingFlags.NonPublic);
                if (targetMethod == null || prefixMethod == null)
                {
                    ModLogger.Info("ListedShellUnloadMissionReceiveOwnershipPatch: target/prefix method not found. Skip.");
                    return;
                }

                ListedShellNetworkBootstrapRuntime.InitializeBaseNetworkContracts(targetType);

                harmony.Patch(targetMethod, prefix: new HarmonyMethod(prefixMethod));
                _isApplied = true;
                ModLogger.Info("ListedShellUnloadMissionReceiveOwnershipPatch: patched BaseNetworkComponent.HandleServerEventUnloadMission.");
            }
            catch (Exception ex)
            {
                ModLogger.Error("ListedShellUnloadMissionReceiveOwnershipPatch.Apply failed.", ex);
            }
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
                "ListedShellUnloadMissionReceiveOwnershipPatch: intercepted listed UnloadMission and bypassed native BaseNetworkComponent unload coroutine. " +
                "UnloadingForBattleIndexMismatch=" + message.UnloadingForBattleIndexMismatch + ".");
            return false;
        }
    }
}
