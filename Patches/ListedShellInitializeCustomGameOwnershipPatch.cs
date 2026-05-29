using System;
using System.Reflection;
using CoopSpectator.Infrastructure;
using HarmonyLib;
using NetworkMessages.FromServer;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.Patches
{
    /// <summary>
    /// Owns the listed-shell InitializeCustomGameMessage send gate so late join no longer
    /// depends on BannerlordNetwork.LobbyMissionType staying in the native custom/community state.
    /// </summary>
    internal static class ListedShellInitializeCustomGameOwnershipPatch
    {
        private const int NativeLobbyMissionTypeMatchmaker = 0;
        private const int NativeLobbyMissionTypeCustom = 1;
        private const int NativeLobbyMissionTypeCommunity = 2;

        private static FieldInfo _baseNetworkComponentDataField;
        private static MethodInfo _ensureBaseNetworkComponentDataMethod;
        private static Type _lobbyMissionTypeEnumType;
        private static PropertyInfo _lobbyMissionTypeProperty;
        private static MethodInfo _startMultiplayerLobbyMissionMethod;

        public static void Apply(Harmony harmony)
        {
            try
            {
                Type targetType = AccessTools.TypeByName("TaleWorlds.MountAndBlade.Multiplayer.NetworkComponents.BaseNetworkComponent");
                if (targetType == null)
                {
                    ModLogger.Info("ListedShellInitializeCustomGameOwnershipPatch: BaseNetworkComponent type not found. Skip.");
                    return;
                }

                MethodInfo targetMethod = targetType.GetMethod(
                    "HandleNewClientConnect",
                    BindingFlags.Instance | BindingFlags.Public,
                    null,
                    new[] { typeof(PlayerConnectionInfo) },
                    null);
                MethodInfo prefixMethod = typeof(ListedShellInitializeCustomGameOwnershipPatch).GetMethod(
                    nameof(HandleNewClientConnect_Prefix),
                    BindingFlags.Static | BindingFlags.NonPublic);
                MethodInfo postfixMethod = typeof(ListedShellInitializeCustomGameOwnershipPatch).GetMethod(
                    nameof(HandleNewClientConnect_Postfix),
                    BindingFlags.Static | BindingFlags.NonPublic);
                if (targetMethod == null || prefixMethod == null || postfixMethod == null)
                {
                    ModLogger.Info("ListedShellInitializeCustomGameOwnershipPatch: target/prefix/postfix method not found. Skip.");
                    return;
                }

                _baseNetworkComponentDataField = targetType.GetField("_baseNetworkComponentData", BindingFlags.Instance | BindingFlags.NonPublic);
                _ensureBaseNetworkComponentDataMethod = targetType.GetMethod("EnsureBaseNetworkComponentData", BindingFlags.Instance | BindingFlags.NonPublic);
                _lobbyMissionTypeProperty = typeof(BannerlordNetwork).GetProperty("LobbyMissionType", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                _lobbyMissionTypeEnumType = _lobbyMissionTypeProperty?.PropertyType;
                _startMultiplayerLobbyMissionMethod = typeof(BannerlordNetwork).GetMethod(
                    "StartMultiplayerLobbyMission",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    _lobbyMissionTypeEnumType == null ? Type.EmptyTypes : new[] { _lobbyMissionTypeEnumType },
                    null);

                harmony.Patch(targetMethod, prefix: new HarmonyMethod(prefixMethod), postfix: new HarmonyMethod(postfixMethod));
                ModLogger.Info("ListedShellInitializeCustomGameOwnershipPatch: patched BaseNetworkComponent.HandleNewClientConnect.");
            }
            catch (Exception ex)
            {
                ModLogger.Error("ListedShellInitializeCustomGameOwnershipPatch.Apply failed.", ex);
            }
        }

        private static void HandleNewClientConnect_Prefix(object __instance, PlayerConnectionInfo playerConnectionInfo, ref int __state)
        {
            __state = 0;

            if (!GameNetwork.IsServer)
                return;

            NetworkCommunicator targetPeer = playerConnectionInfo?.NetworkPeer;
            if (targetPeer == null || targetPeer.IsServerPeer)
                return;

            Mission mission = Mission.Current;
            if (!ShouldOwnListedShellInitializeCustomGameIngress(mission))
                return;

            EnsureBaseNetworkComponentData(__instance);

            int currentLobbyMissionType = GetCurrentLobbyMissionTypeCode();
            if (currentLobbyMissionType == NativeLobbyMissionTypeCustom ||
                currentLobbyMissionType == NativeLobbyMissionTypeCommunity)
            {
                if (!TrySetCurrentLobbyMissionTypeCode(NativeLobbyMissionTypeMatchmaker))
                {
                    ModLogger.Info(
                        "ListedShellInitializeCustomGameOwnershipPatch: failed to disarm native LobbyMissionType gate before listed HandleNewClientConnect. " +
                        "Peer=" + (targetPeer.UserName ?? "unknown") +
                        " LobbyMissionTypeCode=" + currentLobbyMissionType + ".");
                    return;
                }

                __state = 2;
                ModLogger.Info(
                    "ListedShellInitializeCustomGameOwnershipPatch: disarmed native LobbyMissionType gate for explicit listed shell before HandleNewClientConnect. " +
                    "Peer=" + (targetPeer.UserName ?? "unknown") +
                    " PreviousLobbyMissionTypeCode=" + currentLobbyMissionType + ".");
                return;
            }

            __state = 1;
        }

        private static void HandleNewClientConnect_Postfix(object __instance, PlayerConnectionInfo playerConnectionInfo, int __state)
        {
            if (__state == 0 || !GameNetwork.IsServer)
                return;

            NetworkCommunicator targetPeer = playerConnectionInfo?.NetworkPeer;
            if (targetPeer == null || targetPeer.IsServerPeer)
                return;

            Mission mission = Mission.Current;
            if (!ShouldOwnListedShellInitializeCustomGameIngress(mission))
                return;

            bool inMission = !GameNetwork.IsDedicatedServer || mission != null;
            string scene = inMission ? (mission?.SceneName ?? string.Empty) : string.Empty;
            string gameType = inMission ? CoopGameModeIds.OfficialTeamDeathmatch : string.Empty;
            int currentBattleIndex = GetCurrentBattleIndex(__instance);

            GameNetwork.BeginModuleEventAsServer(targetPeer);
            GameNetwork.WriteMessage(new InitializeCustomGameMessage(inMission, gameType, scene, currentBattleIndex));
            GameNetwork.EndModuleEventAsServer();

            ModLogger.Info(
                "ListedShellInitializeCustomGameOwnershipPatch: sent coop-owned listed InitializeCustomGameMessage from HandleNewClientConnect. " +
                "Peer=" + (targetPeer.UserName ?? "unknown") +
                " Scene=" + scene +
                " GameType=" + gameType +
                " CurrentBattleIndex=" + currentBattleIndex +
                " OwnershipState=" + __state + ".");
        }

        private static bool ShouldOwnListedShellInitializeCustomGameIngress(Mission mission)
        {
            if (mission == null)
                return false;

            return mission.GetMissionBehavior<ListedShellCompatibilityMode>() != null ||
                mission.GetMissionBehavior<ListedShellCompatibilityModeClient>() != null;
        }

        private static int GetCurrentLobbyMissionTypeCode()
        {
            try
            {
                object value = _lobbyMissionTypeProperty?.GetValue(null);
                if (value == null)
                    return -1;

                return Convert.ToInt32(value);
            }
            catch (Exception ex)
            {
                ModLogger.Info("ListedShellInitializeCustomGameOwnershipPatch: failed to read LobbyMissionType: " + ex.Message);
                return -1;
            }
        }

        private static bool TrySetCurrentLobbyMissionTypeCode(int missionTypeCode)
        {
            try
            {
                if (_startMultiplayerLobbyMissionMethod == null || _lobbyMissionTypeEnumType == null)
                    return false;

                object lobbyMissionType = Enum.ToObject(_lobbyMissionTypeEnumType, missionTypeCode);
                _startMultiplayerLobbyMissionMethod.Invoke(null, new[] { lobbyMissionType });
                return true;
            }
            catch (Exception ex)
            {
                ModLogger.Info("ListedShellInitializeCustomGameOwnershipPatch: failed to set LobbyMissionType via StartMultiplayerLobbyMission: " + ex.Message);
                return false;
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
                ModLogger.Info("ListedShellInitializeCustomGameOwnershipPatch: EnsureBaseNetworkComponentData invoke failed: " + ex.Message);
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
                ModLogger.Info("ListedShellInitializeCustomGameOwnershipPatch: failed to read CurrentBattleIndex: " + ex.Message);
                return -1;
            }
        }
    }
}
