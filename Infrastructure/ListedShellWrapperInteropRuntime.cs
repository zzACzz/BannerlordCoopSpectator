using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.Infrastructure
{
    internal static class ListedShellWrapperInteropRuntime
    {
        private static MethodInfo _platformShowRestrictedInformationMethod;
        private static MethodInfo _platformCheckPrivilegeMethod;
        private static PropertyInfo _platformServicesInstanceProperty;
        private static Type _platformPrivilegeEnumType;
        private static Type _platformPrivilegeResultType;

        public static void InitializeWrapperContracts()
        {
            CachePlatformPrivilegeContracts();
        }

        public static void HandleNativeJoinResult(object resultMessage, string source)
        {
            try
            {
                if (resultMessage == null)
                    return;

                bool success = GetBoolPropertyValue(resultMessage, "Success");
                object response = GetPropertyValue(resultMessage, "Response");
                bool isAdmin = GetBoolPropertyValue(resultMessage, "IsAdmin");
                object joinGameData = GetPropertyValue(resultMessage, "JoinGameData");
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
                        source: source);
                    BattleSnapshotRuntimeState.Clear(source + " join-result");

                    if (string.Equals(gameType, CoopGameModeIds.OfficialTeamDeathmatch, StringComparison.Ordinal))
                    {
                        ListedShellClientSessionOwnershipState.ArmWrapperStart(
                            gameType,
                            serverName,
                            serverAddress,
                            serverPort,
                            sessionKey,
                            peerIndex,
                            source);
                    }
                    else
                    {
                        ListedShellClientSessionOwnershipState.Disarm(source + " non-listed join result");
                    }
                }
                else
                {
                    ListedShellClientSessionOwnershipState.Disarm(source + " failed-or-invalid join result");
                }

                ModLogger.Info(
                    "ListedShellWrapperInteropRuntime: native join result handled. " +
                    "Success=" + success +
                    " Response=" + (response?.ToString() ?? string.Empty) +
                    " ServerName=" + serverName +
                    " HostName=" + hostName +
                    " Address=" + serverAddress +
                    " Port=" + serverPort +
                    " GameType=" + gameType +
                    " IsOfficial=" + isOfficial +
                    " PeerIndex=" + peerIndex +
                    " SessionKey=" + sessionKey +
                    " IsAdmin=" + isAdmin +
                    " ArmedSelfJoin=" + armedSelfJoin +
                    " VpnRedirectApplied=" + vpnRedirectApplied +
                    " VpnRedirectAddress=" + (string.IsNullOrWhiteSpace(vpnRedirectAddress) ? "(none)" : vpnRedirectAddress) +
                    " Source=" + Normalize(source) + ".");
            }
            catch (Exception ex)
            {
                ModLogger.Error("ListedShellWrapperInteropRuntime.HandleNativeJoinResult failed.", ex);
            }
        }

        public static bool ShouldOwnListedShellClientStart()
        {
            return ListedShellClientSessionOwnershipState.ShouldOwnWrapperStart();
        }

        public static bool TryOwnCustomGameClientStart(string source)
        {
            return TryOwnListedShellClientStart(source);
        }

        public static bool TryOwnCommunityClientStart(string source)
        {
            return TryOwnListedShellClientStart(source);
        }

        public static bool TryOwnHostedListedServerStart(string source)
        {
            try
            {
                if (!ListedShellHostedServerStartContextState.TryResolve(
                        out string gameType,
                        out string scene,
                        out bool isInGame))
                {
                    ListedShellHostedServerStartContextState.Disarm(source + " missing-hosted-start-context");
                    return true;
                }

                ListedShellHostedServerStartContextState.Disarm(source + " success");
                _ = RunHostedListedStartAsync(gameType, scene, isInGame, source);
                ModLogger.Info(
                    "ListedShellWrapperInteropRuntime: intercepted hosted listed server wrapper start. " +
                    "GameType=" + gameType +
                    " Scene=" + scene +
                    " IsInGame=" + isInGame +
                    " Source=" + Normalize(source) + ".");
                return false;
            }
            catch (Exception ex)
            {
                ModLogger.Error("ListedShellWrapperInteropRuntime.TryOwnHostedListedServerStart failed.", ex);
                return true;
            }
        }

        public static void HandleHostedServerStartingParameters(object lobbyGameClientHandler, string source)
        {
            try
            {
                object gameClient = GetPropertyValue(lobbyGameClientHandler, "GameClient");
                if (gameClient == null)
                {
                    ListedShellHostedServerStartContextState.Disarm(source + " missing-game-client");
                    return;
                }

                string gameType = Normalize(GetPropertyValue(gameClient, "CustomGameType") as string);
                string scene = Normalize(GetPropertyValue(gameClient, "CustomGameScene") as string);
                bool isInGame = GetPropertyValue(gameClient, "IsInGame") is bool isInGameValue && isInGameValue;

                if (!string.Equals(gameType, CoopGameModeIds.OfficialTeamDeathmatch, StringComparison.Ordinal))
                {
                    ListedShellHostedServerStartContextState.Disarm(source + " non-listed-game-type");
                    return;
                }

                ListedShellHostedServerStartContextState.Arm(gameType, scene, isInGame, source);
                ModLogger.Info(
                    "ListedShellWrapperInteropRuntime: observed hosted listed server starting parameters. " +
                    "GameType=" + gameType +
                    " Scene=" + scene +
                    " IsInGame=" + isInGame +
                    " Source=" + Normalize(source) + ".");
            }
            catch (Exception ex)
            {
                ModLogger.Error("ListedShellWrapperInteropRuntime.HandleHostedServerStartingParameters failed.", ex);
            }
        }

        private static bool TryOwnListedShellClientStart(string source)
        {
            try
            {
                if (!ListedShellClientSessionOwnershipState.TryResolveWrapperStartTransportContext(
                        out string gameType,
                        out string address,
                        out int port,
                        out int sessionKey,
                        out int peerIndex))
                {
                    ListedShellClientSessionOwnershipState.Disarm(source + " missing-wrapper-start-context");
                    return true;
                }

                if (!ListedShellSessionTransportRuntime.TryStartListedClientTransport(
                        gameType,
                        address,
                        port,
                        sessionKey,
                        peerIndex,
                        source))
                {
                    ListedShellClientSessionOwnershipState.Disarm(source + " client-transport-failed");
                    return true;
                }

                TryCheckMultiplayerPrivilege(promptOnRestricted: true);
                ModLogger.Info(
                    "ListedShellWrapperInteropRuntime: owned listed client StartMultiplayer wrapper. " +
                    "Source=" + Normalize(source) +
                    " Address=" + address +
                    " Port=" + port +
                    " SessionKey=" + sessionKey +
                    " PeerIndex=" + peerIndex +
                    " OwnershipRetainedForReceiveBootstrap=True.");
                return false;
            }
            catch (Exception ex)
            {
                ListedShellClientSessionOwnershipState.Disarm(source + " failure");
                ModLogger.Error("ListedShellWrapperInteropRuntime.TryOwnListedShellClientStart failed.", ex);
                return true;
            }
        }

        private static async Task RunHostedListedStartAsync(string gameType, string scene, bool isInGame, string source)
        {
            try
            {
                if (!await ListedShellSessionTransportRuntime.TryStartHostedListedServerTransportAsync(
                        gameType,
                        scene,
                        isInGame,
                        source))
                {
                    return;
                }

                TryCheckMultiplayerPrivilege(promptOnRestricted: false);
            }
            catch (Exception ex)
            {
                ModLogger.Error("ListedShellWrapperInteropRuntime.RunHostedListedStartAsync failed.", ex);
            }
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
                MethodInfo callbackMethod = typeof(ListedShellWrapperInteropRuntime).GetMethod(
                    nameof(OnPrivilegeCheckResult),
                    BindingFlags.Static | BindingFlags.NonPublic);
                object callback = Delegate.CreateDelegate(_platformPrivilegeResultType, callbackMethod);
                _platformCheckPrivilegeMethod.Invoke(platformServicesInstance, new[] { privilege, (object)promptOnRestricted, callback });
            }
            catch (Exception ex)
            {
                ModLogger.Info("ListedShellWrapperInteropRuntime: CheckPrivilege invoke failed: " + ex.Message);
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
                ModLogger.Info("ListedShellWrapperInteropRuntime: ShowRestrictedInformation invoke failed: " + ex.Message);
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
