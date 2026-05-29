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
        private static MethodInfo _setCurrentIntermissionTimerMethod;
        private static MethodInfo _setClientIntermissionStateMethod;
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

                _setCurrentIntermissionTimerMethod = targetType.GetMethod(
                    "set_CurrentIntermissionTimer",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                _setClientIntermissionStateMethod = targetType.GetMethod(
                    "set_ClientIntermissionState",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

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
            if (message == null || !ShouldOwnListedShellUnloadMissionReceive())
                return true;

            _ = HandleListedShellUnloadMissionAsync(__instance, message);
            ModLogger.Info(
                "ListedShellUnloadMissionReceiveOwnershipPatch: intercepted listed UnloadMission and bypassed native BaseNetworkComponent unload coroutine. " +
                "UnloadingForBattleIndexMismatch=" + message.UnloadingForBattleIndexMismatch + ".");
            return false;
        }

        private static bool ShouldOwnListedShellUnloadMissionReceive()
        {
            Mission mission = Mission.Current;
            if (mission != null &&
                (mission.GetMissionBehavior<ListedShellCompatibilityMode>() != null ||
                 mission.GetMissionBehavior<ListedShellCompatibilityModeClient>() != null))
            {
                return true;
            }

            return CustomGameJoinContextState.ShouldOwnListedShellCustomGameBootstrap();
        }

        private static async Task HandleListedShellUnloadMissionAsync(object instance, UnloadMission message)
        {
            try
            {
                if (GameNetwork.MyPeer != null)
                    GameNetwork.MyPeer.IsSynchronized = false;

                ResetClientIntermissionState(instance);

                Mission currentMission = Mission.Current;
                ListedShellMissionLobbyClientComponent listedClient = currentMission?.GetMissionBehavior<ListedShellMissionLobbyClientComponent>();
                listedClient?.SetServerEndingBeforeClientLoaded(message.UnloadingForBattleIndexMismatch);

                BannerlordNetwork.EndMultiplayerLobbyMission();
                Game.Current?.GetGameHandler<ChatBox>()?.ResetMuteList();

                await WaitForMissionUnloadAsync();
                LoadingWindow.DisableGlobalLoadingWindow();
            }
            catch (Exception ex)
            {
                ModLogger.Error("ListedShellUnloadMissionReceiveOwnershipPatch.HandleListedShellUnloadMissionAsync failed.", ex);
            }
        }

        private static async Task WaitForMissionUnloadAsync()
        {
            for (int i = 0; i < 5000; i++)
            {
                if (Mission.Current == null)
                    return;

                await Task.Delay(1);
            }

            ModLogger.Info("ListedShellUnloadMissionReceiveOwnershipPatch: mission unload wait timed out after coop-owned listed unload path.");
        }

        private static void ResetClientIntermissionState(object instance)
        {
            try
            {
                _setCurrentIntermissionTimerMethod?.Invoke(instance, new object[] { 0f });

                if (_setClientIntermissionStateMethod != null)
                {
                    object intermissionState = Enum.ToObject(_setClientIntermissionStateMethod.GetParameters()[0].ParameterType, 0);
                    _setClientIntermissionStateMethod.Invoke(instance, new[] { intermissionState });
                }
            }
            catch (Exception ex)
            {
                ModLogger.Info("ListedShellUnloadMissionReceiveOwnershipPatch: failed to reset client intermission state: " + ex.Message);
            }
        }
    }
}
