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
        private static MethodInfo _syncRelevantGameOptionsToServerMethod;
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

                _syncRelevantGameOptionsToServerMethod = typeof(GameNetwork).GetMethod(
                    "SyncRelevantGameOptionsToServer",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

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
            if (message == null || !ShouldOwnListedShellInitializeCustomGameReceive(message))
                return true;

            _ = InitializeListedCustomGameAsync(__instance, message);
            ModLogger.Info(
                "ListedShellInitializeCustomGameReceiveOwnershipPatch: intercepted listed InitializeCustomGameMessage and bypassed native custom/community client state gate. " +
                "InMission=" + message.InMission +
                " GameType=" + (message.GameType ?? string.Empty) +
                " Map=" + (message.Map ?? string.Empty) +
                " BattleIndex=" + message.BattleIndex + ".");
            return false;
        }

        private static bool ShouldOwnListedShellInitializeCustomGameReceive(InitializeCustomGameMessage message)
        {
            if (message == null)
                return false;

            if (!string.Equals(message.GameType, CoopGameModeIds.OfficialTeamDeathmatch, StringComparison.Ordinal))
                return false;

            return ListedShellTransportBootstrapState.ShouldOwnClientReceiveBootstrap();
        }

        private static async Task InitializeListedCustomGameAsync(object instance, InitializeCustomGameMessage message)
        {
            try
            {
                await Task.Delay(200);
                await WaitForListedBootstrapReadinessAsync();

                if (message.InMission)
                {
                    ModLogger.Info(
                        "ListedShellInitializeCustomGameReceiveOwnershipPatch: starting listed mission from coop-owned receive path. " +
                        "GameType=" + (message.GameType ?? string.Empty) +
                        " Map=" + (message.Map ?? string.Empty) +
                        " BattleIndex=" + message.BattleIndex + ".");

                    ListedShellMissionSessionState.AdoptRemoteMissionToken(
                        message.Map,
                        message.BattleIndex,
                        "ListedShellInitializeCustomGameReceiveOwnershipPatch.InitializeListedCustomGameAsync");
                    if (!TaleWorlds.MountAndBlade.Module.CurrentModule.StartMultiplayerGame(message.GameType, message.Map))
                    {
                        ModLogger.Info("ListedShellInitializeCustomGameReceiveOwnershipPatch: StartMultiplayerGame returned false for listed receive path.");
                    }

                    return;
                }

                LoadingWindow.DisableGlobalLoadingWindow();
                TrySyncRelevantGameOptionsToServer();
                ModLogger.Info("ListedShellInitializeCustomGameReceiveOwnershipPatch: completed listed non-mission InitializeCustomGame receive path.");
            }
            catch (Exception ex)
            {
                ModLogger.Error("ListedShellInitializeCustomGameReceiveOwnershipPatch.InitializeListedCustomGameAsync failed.", ex);
            }
        }

        private static async Task WaitForListedBootstrapReadinessAsync()
        {
            for (int i = 0; i < 2000; i++)
            {
                if (IsListedBootstrapReady())
                    return;

                await Task.Delay(1);
            }

            ModLogger.Info("ListedShellInitializeCustomGameReceiveOwnershipPatch: listed bootstrap readiness wait timed out; proceeding with coop-owned receive path.");
        }

        private static bool IsListedBootstrapReady()
        {
            GameStateManager manager = GameStateManager.Current;
            return manager?.ActiveState != null && TaleWorlds.MountAndBlade.Module.CurrentModule != null;
        }

        private static void TrySyncRelevantGameOptionsToServer()
        {
            try
            {
                _syncRelevantGameOptionsToServerMethod?.Invoke(null, Array.Empty<object>());
            }
            catch (Exception ex)
            {
                ModLogger.Info("ListedShellInitializeCustomGameReceiveOwnershipPatch: SyncRelevantGameOptionsToServer invoke failed: " + ex.Message);
            }
        }
    }
}
