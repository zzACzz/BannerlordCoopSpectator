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

    internal static class CoopLobbyAutomationDriver
    {
        private const string MultiplayerAssemblyName = "TaleWorlds.MountAndBlade.Multiplayer";
        private const string NetworkMainTypeName = "TaleWorlds.MountAndBlade.NetworkMain";

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
                Assembly assembly = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(candidate =>
                        string.Equals(candidate.GetName().Name, MultiplayerAssemblyName, StringComparison.Ordinal));
                if (assembly == null)
                {
                    failureMessage = "The multiplayer assembly is not loaded.";
                    return false;
                }

                Type networkMainType = assembly.GetType(NetworkMainTypeName);
                PropertyInfo gameClientProperty = networkMainType?.GetProperty(
                    "GameClient",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
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
    }
}
