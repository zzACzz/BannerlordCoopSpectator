using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using CoopSpectator.Infrastructure;
using HarmonyLib;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.Patches
{
    /// <summary>
    /// Owns the official custom/community/player-based listed wrappers so explicit listed ingress
    /// no longer depends on native StartMultiplayerLobbyMission(Custom|Community) startup flow.
    /// </summary>
    internal static class ListedShellLobbyGameStateOwnershipPatch
    {
        private const int StartMultiplayerServerSessionPort = 0x270f;

        private static bool _isApplied;
        private static Type _customClientType;
        private static Type _communityClientType;
        private static Type _playerBasedCustomServerType;
        private static FieldInfo _customClientAddressField;
        private static FieldInfo _customClientPortField;
        private static FieldInfo _customClientSessionKeyField;
        private static FieldInfo _customClientPeerIndexField;
        private static FieldInfo _communityClientAddressField;
        private static FieldInfo _communityClientPortField;
        private static FieldInfo _communityClientSessionKeyField;
        private static FieldInfo _communityClientPeerIndexField;
        private static FieldInfo _playerBasedCustomServerGameClientField;
        private static MethodInfo _platformShowRestrictedInformationMethod;
        private static MethodInfo _platformCheckPrivilegeMethod;
        private static PropertyInfo _platformServicesInstanceProperty;
        private static Type _platformPrivilegeEnumType;
        private static Type _platformPrivilegeResultType;

        public static void Apply(Harmony harmony)
        {
            if (_isApplied)
                return;

            try
            {
                _customClientType = AccessTools.TypeByName("TaleWorlds.MountAndBlade.LobbyGameStateCustomGameClient");
                _communityClientType = AccessTools.TypeByName("TaleWorlds.MountAndBlade.LobbyGameStateCommunityClient");
                _playerBasedCustomServerType = AccessTools.TypeByName("TaleWorlds.MountAndBlade.LobbyGameStatePlayerBasedCustomServer");
                if (_customClientType == null || _communityClientType == null || _playerBasedCustomServerType == null)
                {
                    ModLogger.Info("ListedShellLobbyGameStateOwnershipPatch: one or more native lobby game-state types not found. Skip.");
                    return;
                }

                CacheNativeFieldContracts();
                CachePlatformPrivilegeContracts();

                PatchClientStateStart(harmony, _customClientType, nameof(CustomGameClientStartMultiplayer_Prefix));
                PatchClientStateStart(harmony, _communityClientType, nameof(CommunityClientStartMultiplayer_Prefix));

                MethodInfo hostedStartMethod = _playerBasedCustomServerType.GetMethod("HandleServerStartMultiplayer", BindingFlags.Instance | BindingFlags.NonPublic);
                MethodInfo hostedStartPrefixMethod = typeof(ListedShellLobbyGameStateOwnershipPatch).GetMethod(
                    nameof(HandleServerStartMultiplayer_Prefix),
                    BindingFlags.Static | BindingFlags.NonPublic);
                if (hostedStartMethod == null || hostedStartPrefixMethod == null)
                {
                    ModLogger.Info("ListedShellLobbyGameStateOwnershipPatch: hosted custom-server target/prefix not found. Skip.");
                    return;
                }

                harmony.Patch(hostedStartMethod, prefix: new HarmonyMethod(hostedStartPrefixMethod));
                _isApplied = true;
                ModLogger.Info("ListedShellLobbyGameStateOwnershipPatch: patched official listed custom/community/player-based wrappers.");
            }
            catch (Exception ex)
            {
                ModLogger.Error("ListedShellLobbyGameStateOwnershipPatch.Apply failed.", ex);
            }
        }

        private static void PatchClientStateStart(Harmony harmony, Type stateType, string prefixMethodName)
        {
            MethodInfo targetMethod = stateType.GetMethod("StartMultiplayer", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            MethodInfo prefixMethod = typeof(ListedShellLobbyGameStateOwnershipPatch).GetMethod(prefixMethodName, BindingFlags.Static | BindingFlags.NonPublic);
            if (targetMethod == null || prefixMethod == null)
            {
                ModLogger.Info("ListedShellLobbyGameStateOwnershipPatch: client StartMultiplayer target/prefix not found for " + stateType.FullName + ".");
                return;
            }

            harmony.Patch(targetMethod, prefix: new HarmonyMethod(prefixMethod));
        }

        private static bool CustomGameClientStartMultiplayer_Prefix(object __instance)
        {
            if (!ShouldOwnListedShellClientStart())
                return true;

            return TryOwnListedShellClientStart(
                __instance,
                _customClientAddressField,
                _customClientPortField,
                _customClientSessionKeyField,
                _customClientPeerIndexField,
                "LobbyGameStateCustomGameClient");
        }

        private static bool CommunityClientStartMultiplayer_Prefix(object __instance)
        {
            if (!ShouldOwnListedShellClientStart())
                return true;

            return TryOwnListedShellClientStart(
                __instance,
                _communityClientAddressField,
                _communityClientPortField,
                _communityClientSessionKeyField,
                _communityClientPeerIndexField,
                "LobbyGameStateCommunityClient");
        }

        private static bool HandleServerStartMultiplayer_Prefix(object __instance)
        {
            try
            {
                if (!TryResolveHostedListedStartContext(__instance, out string gameType, out string scene, out bool isInGame))
                    return true;

                if (!string.Equals(gameType, CoopGameModeIds.OfficialTeamDeathmatch, StringComparison.Ordinal))
                    return true;

                _ = RunHostedListedStartAsync(gameType, scene, isInGame);
                ModLogger.Info(
                    "ListedShellLobbyGameStateOwnershipPatch: intercepted native LobbyGameStatePlayerBasedCustomServer.HandleServerStartMultiplayer for explicit listed shell. " +
                    "GameType=" + gameType +
                    " Scene=" + scene +
                    " IsInGame=" + isInGame + ".");
                return false;
            }
            catch (Exception ex)
            {
                ModLogger.Error("ListedShellLobbyGameStateOwnershipPatch.HandleServerStartMultiplayer_Prefix failed.", ex);
                return true;
            }
        }

        private static bool TryOwnListedShellClientStart(
            object instance,
            FieldInfo addressField,
            FieldInfo portField,
            FieldInfo sessionKeyField,
            FieldInfo peerIndexField,
            string source)
        {
            try
            {
                string address = addressField?.GetValue(instance) as string ?? string.Empty;
                int port = portField?.GetValue(instance) is int portValue ? portValue : 0;
                int sessionKey = sessionKeyField?.GetValue(instance) is int sessionKeyValue ? sessionKeyValue : 0;
                int peerIndex = peerIndexField?.GetValue(instance) is int peerIndexValue ? peerIndexValue : 0;

                ListedShellTransportBootstrapState.ArmClientReceiveBootstrap(
                    CoopGameModeIds.OfficialTeamDeathmatch,
                    address,
                    port,
                    sessionKey,
                    peerIndex,
                    "ListedShellLobbyGameStateOwnershipPatch.TryOwnListedShellClientStart");
                GameNetwork.StartMultiplayerOnClient(address, port, sessionKey, peerIndex);
                TryCheckMultiplayerPrivilege(promptOnRestricted: true);
                ModLogger.Info(
                    "ListedShellLobbyGameStateOwnershipPatch: owned listed client StartMultiplayer wrapper. " +
                    "Source=" + source +
                    " Address=" + address +
                    " Port=" + port +
                    " SessionKey=" + sessionKey +
                    " PeerIndex=" + peerIndex + ".");
                return false;
            }
            catch (Exception ex)
            {
                ListedShellTransportBootstrapState.DisarmClientReceiveBootstrap(
                    "ListedShellLobbyGameStateOwnershipPatch.TryOwnListedShellClientStart failure");
                ModLogger.Error("ListedShellLobbyGameStateOwnershipPatch.TryOwnListedShellClientStart failed for " + source + ".", ex);
                return true;
            }
        }

        private static async Task RunHostedListedStartAsync(string gameType, string scene, bool isInGame)
        {
            try
            {
                GameNetwork.PreStartMultiplayerOnServer();
                if (!TaleWorlds.MountAndBlade.Module.CurrentModule.StartMultiplayerGame(gameType, scene))
                {
                    ModLogger.Info(
                        "ListedShellLobbyGameStateOwnershipPatch: hosted listed StartMultiplayerGame returned false. " +
                        "GameType=" + gameType +
                        " Scene=" + scene + ".");
                    return;
                }

                while (Mission.Current == null || (int)Mission.Current.CurrentState != 2)
                    await Task.Delay(1);

                GameNetwork.StartMultiplayerOnServer(StartMultiplayerServerSessionPort);
                if (isInGame)
                {
                    BannerlordNetwork.CreateServerPeer();
                    ModLogger.Info("ListedShellLobbyGameStateOwnershipPatch: hosted listed shell created server peer and entered server list visibility.");
                    if (!GameNetwork.IsDedicatedServer)
                        GameNetwork.ClientFinishedLoading(GameNetwork.MyPeer);
                }

                TryCheckMultiplayerPrivilege(promptOnRestricted: false);
            }
            catch (Exception ex)
            {
                ModLogger.Error("ListedShellLobbyGameStateOwnershipPatch.RunHostedListedStartAsync failed.", ex);
            }
        }

        private static bool ShouldOwnListedShellClientStart()
        {
            return CustomGameJoinContextState.ShouldOwnListedShellCustomGameBootstrap();
        }

        private static bool TryResolveHostedListedStartContext(object instance, out string gameType, out string scene, out bool isInGame)
        {
            gameType = string.Empty;
            scene = string.Empty;
            isInGame = false;

            try
            {
                object gameClient = _playerBasedCustomServerGameClientField?.GetValue(instance);
                if (gameClient == null)
                    return false;

                PropertyInfo customGameTypeProperty = gameClient.GetType().GetProperty("CustomGameType", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                PropertyInfo customGameSceneProperty = gameClient.GetType().GetProperty("CustomGameScene", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                PropertyInfo isInGameProperty = gameClient.GetType().GetProperty("IsInGame", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                gameType = Normalize(customGameTypeProperty?.GetValue(gameClient) as string);
                scene = Normalize(customGameSceneProperty?.GetValue(gameClient) as string);
                isInGame = isInGameProperty?.GetValue(gameClient) is bool isInGameValue && isInGameValue;
                return !string.IsNullOrEmpty(gameType);
            }
            catch (Exception ex)
            {
                ModLogger.Info("ListedShellLobbyGameStateOwnershipPatch: failed to resolve hosted listed start context: " + ex.Message);
                return false;
            }
        }

        private static void CacheNativeFieldContracts()
        {
            _customClientAddressField = _customClientType.GetField("_address", BindingFlags.Instance | BindingFlags.NonPublic);
            _customClientPortField = _customClientType.GetField("_port", BindingFlags.Instance | BindingFlags.NonPublic);
            _customClientSessionKeyField = _customClientType.GetField("_sessionKey", BindingFlags.Instance | BindingFlags.NonPublic);
            _customClientPeerIndexField = _customClientType.GetField("_peerIndex", BindingFlags.Instance | BindingFlags.NonPublic);

            _communityClientAddressField = _communityClientType.GetField("_address", BindingFlags.Instance | BindingFlags.NonPublic);
            _communityClientPortField = _communityClientType.GetField("_port", BindingFlags.Instance | BindingFlags.NonPublic);
            _communityClientSessionKeyField = _communityClientType.GetField("_sessionKey", BindingFlags.Instance | BindingFlags.NonPublic);
            _communityClientPeerIndexField = _communityClientType.GetField("_peerIndex", BindingFlags.Instance | BindingFlags.NonPublic);

            _playerBasedCustomServerGameClientField = _playerBasedCustomServerType.GetField("_gameClient", BindingFlags.Instance | BindingFlags.NonPublic);
        }

        private static void CachePlatformPrivilegeContracts()
        {
            Assembly platformAssembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => string.Equals(a.GetName().Name, "TaleWorlds.PlatformService", StringComparison.Ordinal));
            if (platformAssembly == null)
                return;

            Type platformServicesType = platformAssembly.GetType("TaleWorlds.PlatformService.PlatformServices");
            _platformServicesInstanceProperty = platformServicesType?.GetProperty("Instance", BindingFlags.Static | BindingFlags.Public);
            _platformPrivilegeEnumType = platformAssembly.GetType("TaleWorlds.PlatformService.Privilege");
            _platformPrivilegeResultType = platformAssembly.GetType("TaleWorlds.PlatformService.PrivilegeResult");

            object platformServicesInstance = _platformServicesInstanceProperty?.GetValue(null);
            if (platformServicesInstance == null)
                return;

            Type platformServicesInstanceType = platformServicesInstance.GetType();
            _platformCheckPrivilegeMethod = platformServicesInstanceType.GetMethod(
                "CheckPrivilege",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            _platformShowRestrictedInformationMethod = platformServicesInstanceType.GetMethod(
                "ShowRestrictedInformation",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        }

        private static void TryCheckMultiplayerPrivilege(bool promptOnRestricted)
        {
            try
            {
                object platformServicesInstance = _platformServicesInstanceProperty?.GetValue(null);
                if (platformServicesInstance == null ||
                    _platformCheckPrivilegeMethod == null ||
                    _platformPrivilegeEnumType == null ||
                    _platformPrivilegeResultType == null)
                {
                    return;
                }

                object privilege = Enum.ToObject(_platformPrivilegeEnumType, 1);
                MethodInfo callbackMethod = typeof(ListedShellLobbyGameStateOwnershipPatch).GetMethod(
                    nameof(OnPrivilegeCheckResult),
                    BindingFlags.Static | BindingFlags.NonPublic);
                object callback = Delegate.CreateDelegate(_platformPrivilegeResultType, callbackMethod);
                _platformCheckPrivilegeMethod.Invoke(platformServicesInstance, new[] { privilege, (object)promptOnRestricted, callback });
            }
            catch (Exception ex)
            {
                ModLogger.Info("ListedShellLobbyGameStateOwnershipPatch: CheckPrivilege invoke failed: " + ex.Message);
            }
        }

        private static void OnPrivilegeCheckResult(bool result)
        {
            if (result)
                return;

            try
            {
                object platformServicesInstance = _platformServicesInstanceProperty?.GetValue(null);
                _platformShowRestrictedInformationMethod?.Invoke(platformServicesInstance, Array.Empty<object>());
            }
            catch (Exception ex)
            {
                ModLogger.Info("ListedShellLobbyGameStateOwnershipPatch: ShowRestrictedInformation invoke failed: " + ex.Message);
            }
        }

        private static string Normalize(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }
    }
}
