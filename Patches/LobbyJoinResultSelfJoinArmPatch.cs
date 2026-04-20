using System;
using System.Linq;
using System.Reflection;
using CoopSpectator.Infrastructure;
using HarmonyLib;

namespace CoopSpectator.Patches
{
    internal static class LobbyJoinResultSelfJoinArmPatch
    {
        private static bool _isApplied;

        public static void Prefix(object __0)
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
                }

                ModLogger.Info(
                    "LobbyJoinResultSelfJoinArmPatch: native join result handled. " +
                    "success=" + success +
                    " response=" + (response?.ToString() ?? string.Empty) +
                    " serverName=" + serverName +
                    " hostName=" + hostName +
                    " address=" + serverAddress +
                    " port=" + serverPort +
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
                ModLogger.Error("LobbyJoinResultSelfJoinArmPatch.Prefix failed.", ex);
            }
        }

        public static void Apply(Harmony harmony)
        {
            if (_isApplied)
                return;

            try
            {
                Assembly diamondAssembly = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name == "TaleWorlds.MountAndBlade.Diamond");
                if (diamondAssembly == null)
                {
                    ModLogger.Info("LobbyJoinResultSelfJoinArmPatch: TaleWorlds.MountAndBlade.Diamond not loaded, skip.");
                    return;
                }

                Type lobbyClientType = diamondAssembly.GetType("TaleWorlds.MountAndBlade.Diamond.LobbyClient");
                if (lobbyClientType == null)
                {
                    ModLogger.Info("LobbyJoinResultSelfJoinArmPatch: LobbyClient type not found.");
                    return;
                }

                MethodInfo method = lobbyClientType
                    .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
                    .FirstOrDefault(m => m.Name == "OnJoinCustomGameResultMessage" && m.GetParameters().Length == 1);
                if (method == null)
                {
                    ModLogger.Info("LobbyJoinResultSelfJoinArmPatch: OnJoinCustomGameResultMessage not found.");
                    return;
                }

                MethodInfo prefix = typeof(LobbyJoinResultSelfJoinArmPatch).GetMethod(nameof(Prefix), BindingFlags.Public | BindingFlags.Static);
                harmony.Patch(method, prefix: new HarmonyMethod(prefix));
                _isApplied = true;
                ModLogger.Info("LobbyJoinResultSelfJoinArmPatch: applied to LobbyClient.OnJoinCustomGameResultMessage.");
            }
            catch (Exception ex)
            {
                ModLogger.Error("LobbyJoinResultSelfJoinArmPatch.Apply failed.", ex);
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
    }
}
