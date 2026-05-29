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

                _setCurrentIntermissionTimerMethod = targetType.GetMethod(
                    "set_CurrentIntermissionTimer",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                _setClientIntermissionStateMethod = targetType.GetMethod(
                    "set_ClientIntermissionState",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

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
            if (message == null || !ShouldOwnListedShellLoadMissionReceive(message))
                return true;

            _ = HandleListedShellLoadMissionAsync(__instance, message);
            ModLogger.Info(
                "ListedShellLoadMissionReceiveOwnershipPatch: intercepted listed LoadMission and bypassed native BaseNetworkComponent mission-open path. " +
                "GameType=" + (message.GameType ?? string.Empty) +
                " Map=" + (message.Map ?? string.Empty) +
                " BattleIndex=" + message.BattleIndex + ".");
            return false;
        }

        private static bool ShouldOwnListedShellLoadMissionReceive(LoadMission message)
        {
            if (message == null)
                return false;

            if (!string.Equals(message.GameType, CoopGameModeIds.OfficialTeamDeathmatch, StringComparison.Ordinal))
                return false;

            return CustomGameJoinContextState.ShouldOwnListedShellCustomGameBootstrap();
        }

        private static async Task HandleListedShellLoadMissionAsync(object instance, LoadMission message)
        {
            try
            {
                ListedShellMissionSessionState.AdoptRemoteMissionToken(
                    message.Map,
                    message.BattleIndex,
                    "ListedShellLoadMissionReceiveOwnershipPatch.HandleListedShellLoadMissionAsync");

                await WaitForListedMissionOpenReadinessAsync(message.Map, message.BattleIndex);

                if (GameNetwork.MyPeer != null)
                    GameNetwork.MyPeer.IsSynchronized = false;

                ResetClientIntermissionState(instance);

                if (IsMatchingListedMissionAlreadyActive(message.Map, message.BattleIndex))
                {
                    ModLogger.Info(
                        "ListedShellLoadMissionReceiveOwnershipPatch: listed mission already active for received LoadMission; skipped duplicate StartMultiplayerGame. " +
                        "Map=" + (message.Map ?? string.Empty) +
                        " BattleIndex=" + message.BattleIndex + ".");
                    return;
                }

                if (!TaleWorlds.MountAndBlade.Module.CurrentModule.StartMultiplayerGame(message.GameType, message.Map))
                {
                    ModLogger.Info(
                        "ListedShellLoadMissionReceiveOwnershipPatch: StartMultiplayerGame returned false for listed LoadMission receive path. " +
                        "GameType=" + (message.GameType ?? string.Empty) +
                        " Map=" + (message.Map ?? string.Empty) + ".");
                }
            }
            catch (Exception ex)
            {
                ModLogger.Error("ListedShellLoadMissionReceiveOwnershipPatch.HandleListedShellLoadMissionAsync failed.", ex);
            }
        }

        private static async Task WaitForListedMissionOpenReadinessAsync(string sceneName, int token)
        {
            for (int i = 0; i < 2000; i++)
            {
                if (IsMatchingListedMissionAlreadyActive(sceneName, token))
                    return;

                GameState activeState = GameStateManager.Current?.ActiveState;
                if (!(activeState is MissionState))
                    return;

                await Task.Delay(1);
            }

            ModLogger.Info(
                "ListedShellLoadMissionReceiveOwnershipPatch: listed LoadMission readiness wait timed out; proceeding with coop-owned receive path. " +
                "Scene=" + Normalize(sceneName) +
                " BattleIndex=" + token + ".");
        }

        private static bool IsMatchingListedMissionAlreadyActive(string sceneName, int token)
        {
            Mission currentMission = Mission.Current;
            if (currentMission == null)
                return false;

            if (currentMission.GetMissionBehavior<ListedShellCompatibilityMode>() == null &&
                currentMission.GetMissionBehavior<ListedShellCompatibilityModeClient>() == null)
            {
                return false;
            }

            if (!string.Equals(Normalize(currentMission.SceneName), Normalize(sceneName), StringComparison.Ordinal))
                return false;

            return ListedShellMissionSessionState.TryResolveTransportToken(currentMission, out int currentToken) &&
                currentToken == token;
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
                ModLogger.Info("ListedShellLoadMissionReceiveOwnershipPatch: failed to reset client intermission state: " + ex.Message);
            }
        }

        private static string Normalize(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }
    }
}
