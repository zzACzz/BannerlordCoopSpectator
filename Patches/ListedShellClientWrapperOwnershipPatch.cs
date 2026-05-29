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
    /// Owns the listed client wrapper-entry chain from Diamond join-result through official
    /// custom/community/player-based wrapper start so explicit listed ingress no longer depends
    /// on split patch surfaces for arm/start ownership.
    /// </summary>
    internal static class ListedShellClientWrapperOwnershipPatch
    {
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
                PatchDiamondJoinResult(harmony);
                PatchListedWrapperStarts(harmony);
                _isApplied = true;
                ModLogger.Info("ListedShellClientWrapperOwnershipPatch: patched listed client wrapper-entry chain.");
            }
            catch (Exception ex)
            {
                ModLogger.Error("ListedShellClientWrapperOwnershipPatch.Apply failed.", ex);
            }
        }

        private static void PatchDiamondJoinResult(Harmony harmony)
        {
            Assembly diamondAssembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "TaleWorlds.MountAndBlade.Diamond");
            if (diamondAssembly == null)
            {
                ModLogger.Info("ListedShellClientWrapperOwnershipPatch: TaleWorlds.MountAndBlade.Diamond not loaded, skip join-result patch.");
                return;
            }

            Type lobbyClientType = diamondAssembly.GetType("TaleWorlds.MountAndBlade.Diamond.LobbyClient");
            if (lobbyClientType == null)
            {
                ModLogger.Info("ListedShellClientWrapperOwnershipPatch: LobbyClient type not found.");
                return;
            }

            MethodInfo method = lobbyClientType
                .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
                .FirstOrDefault(m => m.Name == "OnJoinCustomGameResultMessage" && m.GetParameters().Length == 1);
            MethodInfo prefix = typeof(ListedShellClientWrapperOwnershipPatch).GetMethod(
                nameof(OnJoinCustomGameResultMessage_Prefix),
                BindingFlags.Static | BindingFlags.NonPublic);
            if (method == null || prefix == null)
            {
                ModLogger.Info("ListedShellClientWrapperOwnershipPatch: OnJoinCustomGameResultMessage target/prefix not found.");
                return;
            }

            harmony.Patch(method, prefix: new HarmonyMethod(prefix));
        }

        private static void PatchListedWrapperStarts(Harmony harmony)
        {
            _customClientType = AccessTools.TypeByName("TaleWorlds.MountAndBlade.LobbyGameStateCustomGameClient");
            _communityClientType = AccessTools.TypeByName("TaleWorlds.MountAndBlade.LobbyGameStateCommunityClient");
            _playerBasedCustomServerType = AccessTools.TypeByName("TaleWorlds.MountAndBlade.LobbyGameStatePlayerBasedCustomServer");
            if (_customClientType == null || _communityClientType == null || _playerBasedCustomServerType == null)
            {
                ModLogger.Info("ListedShellClientWrapperOwnershipPatch: one or more native lobby game-state types not found. Skip wrapper patch.");
                return;
            }

            CacheNativeFieldContracts();
            CachePlatformPrivilegeContracts();

            PatchClientStateStart(harmony, _customClientType, nameof(CustomGameClientStartMultiplayer_Prefix));
            PatchClientStateStart(harmony, _communityClientType, nameof(CommunityClientStartMultiplayer_Prefix));

            MethodInfo hostedStartMethod = _playerBasedCustomServerType.GetMethod("HandleServerStartMultiplayer", BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo hostedStartPrefixMethod = typeof(ListedShellClientWrapperOwnershipPatch).GetMethod(
                nameof(HandleServerStartMultiplayer_Prefix),
                BindingFlags.Static | BindingFlags.NonPublic);
            if (hostedStartMethod == null || hostedStartPrefixMethod == null)
            {
                ModLogger.Info("ListedShellClientWrapperOwnershipPatch: hosted custom-server target/prefix not found. Skip.");
                return;
            }

            harmony.Patch(hostedStartMethod, prefix: new HarmonyMethod(hostedStartPrefixMethod));
        }

        private static void PatchClientStateStart(Harmony harmony, Type stateType, string prefixMethodName)
        {
            MethodInfo targetMethod = stateType.GetMethod("StartMultiplayer", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            MethodInfo prefixMethod = typeof(ListedShellClientWrapperOwnershipPatch).GetMethod(prefixMethodName, BindingFlags.Static | BindingFlags.NonPublic);
            if (targetMethod == null || prefixMethod == null)
            {
                ModLogger.Info("ListedShellClientWrapperOwnershipPatch: client StartMultiplayer target/prefix not found for " + stateType.FullName + ".");
                return;
            }

            harmony.Patch(targetMethod, prefix: new HarmonyMethod(prefixMethod));
        }

        private static void OnJoinCustomGameResultMessage_Prefix(object __0)
        {
            try
            {
                if (__0 == null)
                    return;

                bool success = GetBoolPropertyValue(__0, "Success");
                object response = GetPropertyValue(__0, "Response");
                bool isAdmin = GetBoolPropertyValue(__0, "IsAdmin");
                object joinGameData = GetPropertyValue(__0, "JoinGameData");
                object gameServerProperties = GetPropertyValue(joinGameData, "GameServerProperties");
                string serverName = GetStringPropertyValue(gameServerProperties, "Name");
                string hostName = GetStringPropertyValue(gameServerProperties, "HostName");
                string serverAddress = GetStringPropertyValue(gameServerProperties, "Address");
                int serverPort = GetIntPropertyValue(gameServerProperties, "Port");
                string gameType = GetStringPropertyValue(gameServerProperties, "GameType");
                bool isOfficial = GetBoolPropertyValue(gameServerProperties, "IsOfficial");
                int peerIndex = GetIntPropertyValue(joinGameData, "PeerIndex");
                int sessionKey = GetIntPropertyValue(joinGameData, "SessionKey");
                bool armedSelfJoin = false;
                bool vpnRedirectApplied = false;
                string vpnRedirectAddress = string.Empty;

                if (success && serverPort > 0)
                {
                    armedSelfJoin = HostSelfJoinRedirectState.ArmForNextJoinIfCurrentHost(serverName, serverAddress, serverPort);
                    if (armedSelfJoin)
                    {
                        PendingCustomGameJoinAddressOverrideState.Clear("host-self-join");
                    }
                    else if (PendingCustomGameJoinAddressOverrideState.TryConsume(serverName, serverAddress, serverPort, out vpnRedirectAddress))
                    {
                        SetPropertyValue(gameServerProperties, "Address", vpnRedirectAddress);
                        serverAddress = vpnRedirectAddress;
                        vpnRedirectApplied = true;
                    }

                    CustomGameJoinContextState.Update(
                        serverName,
                        serverAddress,
                        serverPort,
                        gameType,
                        isOfficial,
                        allowLocalBattleRosterFileFallback: armedSelfJoin,
                        source: "ListedShellClientWrapperOwnershipPatch");
                    BattleSnapshotRuntimeState.Clear("ListedShellClientWrapperOwnershipPatch join-result");

                    if (string.Equals(gameType, CoopGameModeIds.OfficialTeamDeathmatch, StringComparison.Ordinal))
                    {
                        ListedShellClientSessionOwnershipState.ArmWrapperStart(
                            gameType,
                            serverName,
                            serverAddress,
                            serverPort,
                            "ListedShellClientWrapperOwnershipPatch");
                    }
                    else
                    {
                        ListedShellClientSessionOwnershipState.Disarm(
                            "ListedShellClientWrapperOwnershipPatch non-listed join result");
                    }
                }
                else
                {
                    ListedShellClientSessionOwnershipState.Disarm(
                        "ListedShellClientWrapperOwnershipPatch failed-or-invalid join result");
                }

                ModLogger.Info(
                    "ListedShellClientWrapperOwnershipPatch: native join result handled. " +
                    "success=" + success +
                    " response=" + (response?.ToString() ?? string.Empty) +
                    " serverName=" + serverName +
                    " hostName=" + hostName +
                    " address=" + serverAddress +
                    " port=" + serverPort +
                    " gameType=" + gameType +
                    " isOfficial=" + isOfficial +
                    " peerIndex=" + peerIndex +
                    " sessionKey=" + sessionKey +
                    " isAdmin=" + isAdmin +
                    " armedSelfJoin=" + armedSelfJoin +
                    " vpnRedirectApplied=" + vpnRedirectApplied +
                    " vpnRedirectAddress=" + (string.IsNullOrWhiteSpace(vpnRedirectAddress) ? "(none)" : vpnRedirectAddress) + ".");
            }
            catch (Exception ex)
            {
                ModLogger.Error("ListedShellClientWrapperOwnershipPatch.OnJoinCustomGameResultMessage_Prefix failed.", ex);
            }
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
                    "ListedShellClientWrapperOwnershipPatch: intercepted native LobbyGameStatePlayerBasedCustomServer.HandleServerStartMultiplayer for explicit listed shell. " +
                    "GameType=" + gameType +
                    " Scene=" + scene +
                    " IsInGame=" + isInGame + ".");
                return false;
            }
            catch (Exception ex)
            {
                ModLogger.Error("ListedShellClientWrapperOwnershipPatch.HandleServerStartMultiplayer_Prefix failed.", ex);
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

                if (!ListedShellSessionTransportRuntime.TryStartListedClientTransport(
                    CoopGameModeIds.OfficialTeamDeathmatch,
                    address,
                    port,
                    sessionKey,
                    peerIndex,
                    "ListedShellClientWrapperOwnershipPatch.TryOwnListedShellClientStart"))
                {
                    ListedShellClientSessionOwnershipState.Disarm(
                        "ListedShellClientWrapperOwnershipPatch.TryOwnListedShellClientStart client-transport-failed");
                    return true;
                }

                ListedShellClientSessionOwnershipState.Disarm(
                    "ListedShellClientWrapperOwnershipPatch.TryOwnListedShellClientStart success");
                TryCheckMultiplayerPrivilege(promptOnRestricted: true);
                ModLogger.Info(
                    "ListedShellClientWrapperOwnershipPatch: owned listed client StartMultiplayer wrapper. " +
                    "Source=" + source +
                    " Address=" + address +
                    " Port=" + port +
                    " SessionKey=" + sessionKey +
                    " PeerIndex=" + peerIndex + ".");
                return false;
            }
            catch (Exception ex)
            {
                ListedShellClientSessionOwnershipState.Disarm(
                    "ListedShellClientWrapperOwnershipPatch.TryOwnListedShellClientStart failure");
                ModLogger.Error("ListedShellClientWrapperOwnershipPatch.TryOwnListedShellClientStart failed for " + source + ".", ex);
                return true;
            }
        }

        private static async Task RunHostedListedStartAsync(string gameType, string scene, bool isInGame)
        {
            try
            {
                if (!await ListedShellSessionTransportRuntime.TryStartHostedListedServerTransportAsync(
                        gameType,
                        scene,
                        isInGame,
                        "ListedShellClientWrapperOwnershipPatch.RunHostedListedStartAsync"))
                {
                    return;
                }

                TryCheckMultiplayerPrivilege(promptOnRestricted: false);
            }
            catch (Exception ex)
            {
                ModLogger.Error("ListedShellClientWrapperOwnershipPatch.RunHostedListedStartAsync failed.", ex);
            }
        }

        private static bool ShouldOwnListedShellClientStart()
        {
            return ListedShellClientSessionOwnershipState.ShouldOwnWrapperStart();
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
                ModLogger.Info("ListedShellClientWrapperOwnershipPatch: failed to resolve hosted listed start context: " + ex.Message);
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
                MethodInfo callbackMethod = typeof(ListedShellClientWrapperOwnershipPatch).GetMethod(
                    nameof(OnPrivilegeCheckResult),
                    BindingFlags.Static | BindingFlags.NonPublic);
                object callback = Delegate.CreateDelegate(_platformPrivilegeResultType, callbackMethod);
                _platformCheckPrivilegeMethod.Invoke(platformServicesInstance, new[] { privilege, (object)promptOnRestricted, callback });
            }
            catch (Exception ex)
            {
                ModLogger.Info("ListedShellClientWrapperOwnershipPatch: CheckPrivilege invoke failed: " + ex.Message);
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
                ModLogger.Info("ListedShellClientWrapperOwnershipPatch: ShowRestrictedInformation invoke failed: " + ex.Message);
            }
        }

        private static object GetPropertyValue(object target, string propertyName)
        {
            if (target == null)
                return null;

            PropertyInfo property = target.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return property?.GetValue(target);
        }

        private static void SetPropertyValue(object target, string propertyName, object value)
        {
            if (target == null)
                return;

            PropertyInfo property = target.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property != null && property.CanWrite)
                property.SetValue(target, value);
        }

        private static int GetIntPropertyValue(object target, string propertyName)
        {
            object value = GetPropertyValue(target, propertyName);
            return value is int intValue ? intValue : 0;
        }

        private static bool GetBoolPropertyValue(object target, string propertyName)
        {
            object value = GetPropertyValue(target, propertyName);
            return value is bool boolValue && boolValue;
        }

        private static string GetStringPropertyValue(object target, string propertyName)
        {
            return GetPropertyValue(target, propertyName) as string ?? string.Empty;
        }

        private static string Normalize(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }
    }
}
