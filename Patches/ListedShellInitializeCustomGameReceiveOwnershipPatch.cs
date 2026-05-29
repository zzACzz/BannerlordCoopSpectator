using System;
using System.Reflection;
using System.Threading.Tasks;
using CoopSpectator.Infrastructure;
using HarmonyLib;
using NetworkMessages.FromServer;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.Patches
{
    /// <summary>
    /// Owns the listed-shell InitializeCustomGame receive path so bootstrap no longer hard-depends
    /// on the active game state being native LobbyGameStateCustomGameClient/CommunityClient.
    /// </summary>
    internal static class ListedShellInitializeCustomGameReceiveOwnershipPatch
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
                    ModLogger.Info("ListedShellInitializeCustomGameReceiveOwnershipPatch: BaseNetworkComponent type not found. Skip.");
                    return;
                }

                MethodInfo targetMethod = targetType.GetMethod(
                    "HandleServerEventInitializeCustomGame",
                    BindingFlags.Instance | BindingFlags.NonPublic,
                    null,
                    new[] { typeof(TaleWorlds.MountAndBlade.Network.Messages.GameNetworkMessage) },
                    null);
                MethodInfo prefixMethod = typeof(ListedShellInitializeCustomGameReceiveOwnershipPatch).GetMethod(
                    nameof(HandleServerEventInitializeCustomGame_Prefix),
                    BindingFlags.Static | BindingFlags.NonPublic);
                if (targetMethod == null || prefixMethod == null)
                {
                    ModLogger.Info("ListedShellInitializeCustomGameReceiveOwnershipPatch: target/prefix method not found. Skip.");
                    return;
                }

                ListedShellNetworkBootstrapRuntime.InitializeBaseNetworkContracts(targetType);

                harmony.Patch(targetMethod, prefix: new HarmonyMethod(prefixMethod));
                _isApplied = true;
                ModLogger.Info("ListedShellInitializeCustomGameReceiveOwnershipPatch: patched BaseNetworkComponent.HandleServerEventInitializeCustomGame.");
            }
            catch (Exception ex)
            {
                ModLogger.Error("ListedShellInitializeCustomGameReceiveOwnershipPatch.Apply failed.", ex);
            }
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
                "ListedShellInitializeCustomGameReceiveOwnershipPatch: intercepted listed InitializeCustomGameMessage and bypassed native custom/community client state gate. " +
                "InMission=" + message.InMission +
                " GameType=" + (message.GameType ?? string.Empty) +
                " Map=" + (message.Map ?? string.Empty) +
                " BattleIndex=" + message.BattleIndex + ".");
            return false;
        }

    }
}
