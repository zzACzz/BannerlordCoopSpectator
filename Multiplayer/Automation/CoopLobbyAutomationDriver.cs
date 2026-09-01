using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using CoopSpectator.Infrastructure.Automation;

namespace CoopSpectator.Multiplayer.Automation
{
    internal sealed class CoopObservedLobbyServer
    {
        public object NativeServerId { get; set; }
        public CoopAutomationServerDescriptor Descriptor { get; set; }
    }

    internal sealed class CoopPlatformLoginContext
    {
        public object LobbyState { get; set; }
        public object LobbyClient { get; set; }
        public string LobbyClientState { get; set; }
        public bool IsLoggingIn { get; set; }
        public bool? HasMultiplayerPrivilege { get; set; }
    }

    internal static class CoopLobbyAutomationDriver
    {
        internal const string CoreAssemblyName = "TaleWorlds.Core";
        internal const string GameTypeName = "TaleWorlds.Core.Game";
        internal const string NetworkMainAssemblyName = "TaleWorlds.MountAndBlade";
        internal const string NetworkMainTypeName = "TaleWorlds.MountAndBlade.NetworkMain";
        internal const string LobbyStateAssemblyName = "TaleWorlds.MountAndBlade.Multiplayer";
        internal const string LobbyStateTypeName = "TaleWorlds.MountAndBlade.LobbyState";

        public static bool TryGetLobbyClient(
            out object lobbyClient,
            out string lobbyState,
            out string failureMessage)
        {
            lobbyClient = null;
            lobbyState = string.Empty;
            failureMessage = string.Empty;

            try
            {
                if (!TryResolveNetworkMainType(
                        AppDomain.CurrentDomain.GetAssemblies(),
                        out Type networkMainType,
                        out failureMessage))
                {
                    return false;
                }

                PropertyInfo gameClientProperty = networkMainType.GetProperty(
                    "GameClient",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (gameClientProperty == null)
                {
                    failureMessage = "The NetworkMain.GameClient property was not found in " +
                                     NetworkMainAssemblyName + ".";
                    return false;
                }

                lobbyClient = gameClientProperty?.GetValue(null);
                if (lobbyClient == null)
                {
                    failureMessage = "NetworkMain.GameClient is not available yet.";
                    return false;
                }

                lobbyState = GetPropertyValue(lobbyClient, "CurrentState")?.ToString() ?? string.Empty;
                return true;
            }
            catch (Exception ex)
            {
                failureMessage = "Lobby client discovery failed: " + ex.Message;
                return false;
            }
        }

        internal static bool TryResolveNetworkMainType(
            IEnumerable<Assembly> assemblies,
            out Type networkMainType,
            out string failureMessage)
        {
            return TryResolveExactType(
                assemblies,
                NetworkMainAssemblyName,
                NetworkMainTypeName,
                out networkMainType,
                out failureMessage);
        }

        internal static bool TryResolveLobbyStateType(
            IEnumerable<Assembly> assemblies,
            out Type lobbyStateType,
            out string failureMessage)
        {
            return TryResolveExactType(
                assemblies,
                LobbyStateAssemblyName,
                LobbyStateTypeName,
                out lobbyStateType,
                out failureMessage);
        }

        public static bool TryGetPlatformLoginContext(
            object expectedLobbyClient,
            out CoopPlatformLoginContext context,
            out string failureCode,
            out string failureMessage)
        {
            context = null;
            failureCode = string.Empty;
            failureMessage = string.Empty;

            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            if (!TryResolveExactType(
                    assemblies,
                    CoreAssemblyName,
                    GameTypeName,
                    out Type gameType,
                    out failureMessage))
            {
                failureCode = "PlatformLoginGameTypeMissing";
                return false;
            }

            if (!TryResolveLobbyStateType(assemblies, out Type lobbyStateType, out failureMessage))
            {
                failureCode = "PlatformLoginLobbyStateTypeMissing";
                return false;
            }

            return TryGetPlatformLoginContext(
                gameType,
                lobbyStateType,
                expectedLobbyClient,
                out context,
                out failureCode,
                out failureMessage);
        }

        internal static bool TryGetPlatformLoginContext(
            Type gameType,
            Type lobbyStateType,
            object expectedLobbyClient,
            out CoopPlatformLoginContext context,
            out string failureCode,
            out string failureMessage)
        {
            context = null;
            failureCode = string.Empty;
            failureMessage = string.Empty;

            try
            {
                if (gameType == null)
                    return Fail("PlatformLoginGameTypeMissing", "The exact TaleWorlds.Core.Game type is unavailable.", out failureCode, out failureMessage);
                if (lobbyStateType == null)
                    return Fail("PlatformLoginLobbyStateTypeMissing", "The exact TaleWorlds.MountAndBlade.LobbyState type is unavailable.", out failureCode, out failureMessage);
                if (expectedLobbyClient == null)
                    return Fail("PlatformLoginLobbyClientMissing", "NetworkMain.GameClient is unavailable.", out failureCode, out failureMessage);

                PropertyInfo currentProperty = gameType.GetProperty(
                    "Current",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (currentProperty == null)
                    return Fail("PlatformLoginGameCurrentPropertyMissing", "Game.Current was not found.", out failureCode, out failureMessage);

                object game = currentProperty.GetValue(null);
                if (game == null)
                    return Fail("PlatformLoginGameNotReady", "Game.Current is not available yet.", out failureCode, out failureMessage);

                object gameStateManager = GetPropertyValue(game, "GameStateManager");
                if (gameStateManager == null)
                    return Fail("PlatformLoginGameStateManagerNotReady", "Game.Current.GameStateManager is not available yet.", out failureCode, out failureMessage);

                object activeState = GetPropertyValue(gameStateManager, "ActiveState");
                if (activeState == null)
                    return Fail("PlatformLoginActiveStateNotReady", "The active game state is not available yet.", out failureCode, out failureMessage);
                if (activeState.GetType() != lobbyStateType)
                {
                    return Fail(
                        "PlatformLoginLobbyStateNotActive",
                        "The exact active game state is " + activeState.GetType().FullName + ", not " + LobbyStateTypeName + ".",
                        out failureCode,
                        out failureMessage);
                }

                PropertyInfo lobbyClientProperty = lobbyStateType.GetProperty(
                    "LobbyClient",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (lobbyClientProperty == null)
                    return Fail("PlatformLoginLobbyClientPropertyMissing", "LobbyState.LobbyClient was not found.", out failureCode, out failureMessage);

                object stateLobbyClient = lobbyClientProperty.GetValue(activeState);
                if (!ReferenceEquals(stateLobbyClient, expectedLobbyClient))
                {
                    return Fail(
                        "PlatformLoginLobbyClientMismatch",
                        "The active LobbyState does not own the resolved NetworkMain.GameClient instance.",
                        out failureCode,
                        out failureMessage);
                }

                object isLoggingInValue = GetPropertyValue(activeState, "IsLoggingIn");
                if (!(isLoggingInValue is bool isLoggingIn))
                    return Fail("PlatformLoginStateShapeInvalid", "LobbyState.IsLoggingIn is unavailable or is not Boolean.", out failureCode, out failureMessage);

                object privilegeValue = GetPropertyValue(activeState, "HasMultiplayerPrivilege");
                if (privilegeValue != null && !(privilegeValue is bool))
                    return Fail("PlatformLoginStateShapeInvalid", "LobbyState.HasMultiplayerPrivilege is not nullable Boolean.", out failureCode, out failureMessage);

                context = new CoopPlatformLoginContext
                {
                    LobbyState = activeState,
                    LobbyClient = stateLobbyClient,
                    LobbyClientState = GetPropertyValue(expectedLobbyClient, "CurrentState")?.ToString() ?? string.Empty,
                    IsLoggingIn = isLoggingIn,
                    HasMultiplayerPrivilege = privilegeValue == null ? (bool?)null : (bool)privilegeValue
                };
                return true;
            }
            catch (TargetInvocationException ex)
            {
                return Fail(
                    "PlatformLoginContextFailed",
                    "Platform login context discovery failed: " + (ex.InnerException?.Message ?? ex.Message),
                    out failureCode,
                    out failureMessage);
            }
            catch (Exception ex)
            {
                return Fail(
                    "PlatformLoginContextFailed",
                    "Platform login context discovery failed: " + ex.Message,
                    out failureCode,
                    out failureMessage);
            }
        }

        public static bool TryStartPlatformLogin(
            CoopPlatformLoginContext context,
            out Task task,
            out string failureCode,
            out string failureMessage)
        {
            task = null;
            failureCode = string.Empty;
            failureMessage = string.Empty;

            if (context?.LobbyState == null || context.LobbyClient == null)
                return Fail("PlatformLoginContextMissing", "The exact platform login context is missing.", out failureCode, out failureMessage);
            if (!string.Equals(context.LobbyClientState, "Idle", StringComparison.Ordinal))
                return Fail("PlatformLoginClientNotIdle", "The native lobby client is not Idle.", out failureCode, out failureMessage);
            if (context.IsLoggingIn)
                return Fail("PlatformLoginAlreadyActive", "LobbyState is already performing a login.", out failureCode, out failureMessage);
            if (context.HasMultiplayerPrivilege == false)
                return Fail("PlatformLoginPrivilegeDenied", "The platform has denied multiplayer privilege.", out failureCode, out failureMessage);

            try
            {
                MethodInfo method = context.LobbyState.GetType().GetMethod(
                    "TryLogin",
                    BindingFlags.Instance | BindingFlags.Public,
                    null,
                    Type.EmptyTypes,
                    null);
                if (method == null || !typeof(Task).IsAssignableFrom(method.ReturnType))
                {
                    return Fail(
                        "PlatformLoginMethodMissing",
                        "LobbyState.TryLogin() returning Task was not found.",
                        out failureCode,
                        out failureMessage);
                }

                task = method.Invoke(context.LobbyState, null) as Task;
                if (task == null)
                {
                    return Fail(
                        "PlatformLoginTaskMissing",
                        "LobbyState.TryLogin() did not return a Task.",
                        out failureCode,
                        out failureMessage);
                }

                return true;
            }
            catch (TargetInvocationException ex)
            {
                return Fail(
                    "PlatformLoginStartFailed",
                    "LobbyState.TryLogin() failed synchronously: " + (ex.InnerException?.Message ?? ex.Message),
                    out failureCode,
                    out failureMessage);
            }
            catch (Exception ex)
            {
                return Fail(
                    "PlatformLoginStartFailed",
                    "LobbyState.TryLogin() failed synchronously: " + ex.Message,
                    out failureCode,
                    out failureMessage);
            }
        }

        public static bool IsReadyForServerList(object lobbyClient, string lobbyState)
        {
            if (lobbyClient == null || !string.Equals(lobbyState, "AtLobby", StringComparison.Ordinal))
                return false;

            object canPerform = GetPropertyValue(lobbyClient, "CanPerformLobbyActions");
            return !(canPerform is bool value) || value;
        }

        public static bool TryStartServerListRequest(
            object lobbyClient,
            out Task task,
            out string failureMessage)
        {
            task = null;
            failureMessage = string.Empty;

            try
            {
                MethodInfo method = lobbyClient?.GetType().GetMethod(
                    "GetCustomGameServerList",
                    BindingFlags.Instance | BindingFlags.Public,
                    null,
                    Type.EmptyTypes,
                    null);
                if (method == null)
                {
                    failureMessage = "LobbyClient.GetCustomGameServerList() was not found.";
                    return false;
                }

                task = method.Invoke(lobbyClient, null) as Task;
                if (task == null)
                {
                    failureMessage = "LobbyClient.GetCustomGameServerList() did not return a Task.";
                    return false;
                }

                return true;
            }
            catch (TargetInvocationException ex)
            {
                failureMessage = "Server-list request failed: " + (ex.InnerException?.Message ?? ex.Message);
                return false;
            }
            catch (Exception ex)
            {
                failureMessage = "Server-list request failed: " + ex.Message;
                return false;
            }
        }

        public static bool TryReadServerListResult(
            Task task,
            out List<CoopObservedLobbyServer> servers,
            out string failureMessage)
        {
            servers = new List<CoopObservedLobbyServer>();
            failureMessage = string.Empty;

            if (task == null || !task.IsCompleted)
            {
                failureMessage = "The server-list task has not completed.";
                return false;
            }

            if (task.IsCanceled)
            {
                failureMessage = "The server-list task was cancelled.";
                return false;
            }

            if (task.IsFaulted)
            {
                failureMessage = task.Exception?.GetBaseException().Message ?? "The server-list task failed.";
                return false;
            }

            try
            {
                object availableCustomGames = GetTaskResult(task);
                object entries = GetPropertyValue(availableCustomGames, "CustomGameServerInfos");
                if (!(entries is IEnumerable enumerable))
                    return true;

                foreach (object entry in enumerable)
                {
                    object id = GetPropertyValue(entry, "Id");
                    servers.Add(new CoopObservedLobbyServer
                    {
                        NativeServerId = id,
                        Descriptor = new CoopAutomationServerDescriptor
                        {
                            Id = id?.ToString() ?? string.Empty,
                            ServerName = GetStringProperty(entry, "ServerName"),
                            Address = GetStringProperty(entry, "Address"),
                            Port = GetIntProperty(entry, "Port"),
                            GameType = GetStringProperty(entry, "GameType"),
                            Map = GetStringProperty(entry, "Map"),
                            UniqueMapId = GetStringProperty(entry, "UniqueMapId"),
                            PasswordProtected = GetBoolProperty(entry, "PasswordProtected")
                        }
                    });
                }

                return true;
            }
            catch (Exception ex)
            {
                failureMessage = "The server-list result could not be decoded: " + ex.Message;
                return false;
            }
        }

        public static bool TryStartJoinRequest(
            object lobbyClient,
            object serverId,
            string password,
            out Task task,
            out string failureMessage)
        {
            task = null;
            failureMessage = string.Empty;

            if (lobbyClient == null || serverId == null)
            {
                failureMessage = "The lobby client or selected server id is missing.";
                return false;
            }

            try
            {
                MethodInfo method = lobbyClient.GetType()
                    .GetMethods(BindingFlags.Instance | BindingFlags.Public)
                    .FirstOrDefault(candidate =>
                    {
                        if (!string.Equals(candidate.Name, "RequestJoinCustomGame", StringComparison.Ordinal))
                            return false;

                        ParameterInfo[] parameters = candidate.GetParameters();
                        return parameters.Length == 3 &&
                               parameters[0].ParameterType.IsInstanceOfType(serverId) &&
                               parameters[1].ParameterType == typeof(string) &&
                               parameters[2].ParameterType == typeof(bool);
                    });
                if (method == null)
                {
                    failureMessage = "LobbyClient.RequestJoinCustomGame(serverId, password, isAdmin) was not found.";
                    return false;
                }

                task = method.Invoke(
                    lobbyClient,
                    new[] { serverId, password ?? string.Empty, (object)false }) as Task;
                if (task == null)
                {
                    failureMessage = "LobbyClient.RequestJoinCustomGame did not return a Task.";
                    return false;
                }

                return true;
            }
            catch (TargetInvocationException ex)
            {
                failureMessage = "The join request failed: " + (ex.InnerException?.Message ?? ex.Message);
                return false;
            }
            catch (Exception ex)
            {
                failureMessage = "The join request failed: " + ex.Message;
                return false;
            }
        }

        public static bool TryReadJoinResult(Task task, out bool accepted, out string failureMessage)
        {
            accepted = false;
            failureMessage = string.Empty;

            if (task == null || !task.IsCompleted)
            {
                failureMessage = "The join task has not completed.";
                return false;
            }

            if (task.IsCanceled)
            {
                failureMessage = "The join task was cancelled.";
                return false;
            }

            if (task.IsFaulted)
            {
                failureMessage = task.Exception?.GetBaseException().Message ?? "The join task failed.";
                return false;
            }

            try
            {
                object result = GetTaskResult(task);
                if (!(result is bool boolResult))
                {
                    failureMessage = "The join task returned an unexpected result type.";
                    return false;
                }

                accepted = boolResult;
                return true;
            }
            catch (Exception ex)
            {
                failureMessage = "The join result could not be decoded: " + ex.Message;
                return false;
            }
        }

        private static object GetTaskResult(Task task)
        {
            PropertyInfo resultProperty = task.GetType().GetProperty("Result", BindingFlags.Instance | BindingFlags.Public);
            return resultProperty?.GetValue(task);
        }

        private static object GetPropertyValue(object target, string propertyName)
        {
            if (target == null)
                return null;

            PropertyInfo property = target.GetType().GetProperty(
                propertyName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return property?.GetValue(target);
        }

        private static string GetStringProperty(object target, string propertyName)
        {
            return GetPropertyValue(target, propertyName) as string ?? string.Empty;
        }

        private static int GetIntProperty(object target, string propertyName)
        {
            object value = GetPropertyValue(target, propertyName);
            return value is int intValue ? intValue : 0;
        }

        private static bool GetBoolProperty(object target, string propertyName)
        {
            object value = GetPropertyValue(target, propertyName);
            return value is bool boolValue && boolValue;
        }

        private static bool TryResolveExactType(
            IEnumerable<Assembly> assemblies,
            string assemblyName,
            string typeName,
            out Type resolvedType,
            out string failureMessage)
        {
            resolvedType = null;
            failureMessage = string.Empty;

            Assembly assembly = assemblies?.FirstOrDefault(candidate =>
                string.Equals(candidate.GetName().Name, assemblyName, StringComparison.Ordinal));
            if (assembly == null)
            {
                failureMessage = "The " + assemblyName + " assembly is not loaded.";
                return false;
            }

            resolvedType = assembly.GetType(typeName);
            if (resolvedType == null)
            {
                failureMessage = "The " + typeName + " type was not found in " + assemblyName + ".";
                return false;
            }

            return true;
        }

        private static bool Fail(
            string code,
            string message,
            out string failureCode,
            out string failureMessage)
        {
            failureCode = code;
            failureMessage = message;
            return false;
        }
    }
}
