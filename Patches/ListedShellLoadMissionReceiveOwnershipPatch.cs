using System;
using System.Reflection;
using System.Threading.Tasks;
using CoopSpectator.Infrastructure;
using HarmonyLib;
using NetworkMessages.FromServer;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.Patches
{
    /// <summary>
    /// Owns the listed-shell LoadMission receive path so explicit listed startup no longer depends
    /// on BaseNetworkComponent.HandleServerEventLoadMission for mission-open authority.
    /// </summary>
    internal static class ListedShellLoadMissionReceiveOwnershipPatch
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
                    ModLogger.Info("ListedShellLoadMissionReceiveOwnershipPatch: BaseNetworkComponent type not found. Skip.");
                    return;
                }

                MethodInfo targetMethod = targetType.GetMethod(
                    "HandleServerEventLoadMission",
                    BindingFlags.Instance | BindingFlags.NonPublic,
                    null,
                    new[] { typeof(TaleWorlds.MountAndBlade.Network.Messages.GameNetworkMessage) },
                    null);
                MethodInfo prefixMethod = typeof(ListedShellLoadMissionReceiveOwnershipPatch).GetMethod(
                    nameof(HandleServerEventLoadMission_Prefix),
                    BindingFlags.Static | BindingFlags.NonPublic);
                if (targetMethod == null || prefixMethod == null)
                {
                    ModLogger.Info("ListedShellLoadMissionReceiveOwnershipPatch: target/prefix method not found. Skip.");
                    return;
                }

                ListedShellNetworkBootstrapRuntime.InitializeBaseNetworkContracts(targetType);

                harmony.Patch(targetMethod, prefix: new HarmonyMethod(prefixMethod));
                _isApplied = true;
                ModLogger.Info("ListedShellLoadMissionReceiveOwnershipPatch: patched BaseNetworkComponent.HandleServerEventLoadMission.");
            }
            catch (Exception ex)
            {
                ModLogger.Error("ListedShellLoadMissionReceiveOwnershipPatch.Apply failed.", ex);
            }
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
                "ListedShellLoadMissionReceiveOwnershipPatch: intercepted listed LoadMission and bypassed native BaseNetworkComponent mission-open path. " +
                "GameType=" + (message.GameType ?? string.Empty) +
                " Map=" + (message.Map ?? string.Empty) +
                " BattleIndex=" + message.BattleIndex + ".");
            return false;
        }

    }
}
