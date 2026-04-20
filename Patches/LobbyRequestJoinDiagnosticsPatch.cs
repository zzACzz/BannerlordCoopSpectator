using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using CoopSpectator.DedicatedHelper;
using CoopSpectator.Infrastructure;
using HarmonyLib;

namespace CoopSpectator.Patches
{
    internal static class LobbyRequestJoinDiagnosticsPatch
    {
        private static bool _isApplied;

        public static void Prefix(object __instance, object serverId, string password, bool isJoinAsAdmin)
        {
            try
            {
                string selectedServerId = serverId?.ToString() ?? string.Empty;
                object selectedEntry = FindSelectedServerEntry(__instance, selectedServerId);
                string serverName = GetStringPropertyValue(selectedEntry, "ServerName");
                string serverAddress = GetStringPropertyValue(selectedEntry, "Address");
                int serverPort = GetIntPropertyValue(selectedEntry, "Port");
                string hostName = GetStringPropertyValue(selectedEntry, "HostName");
                string map = GetStringPropertyValue(selectedEntry, "Map");
                string uniqueMapId = GetStringPropertyValue(selectedEntry, "UniqueMapId");
                bool isOfficial = GetBoolPropertyValue(selectedEntry, "IsOfficial");

                DedicatedServerLaunchSettings currentSettings = DedicatedHelperLauncher.GetCurrentLaunchSettings();
                bool vpnRedirectArmed = false;
                string advertisedHostAddress = string.Empty;
                PendingCustomGameJoinAddressOverrideState.Clear();
                if (currentSettings != null && currentSettings.UsesAdvertisedHostOverride())
                {
                    advertisedHostAddress = currentSettings.AdvertisedHostAddress ?? string.Empty;
                    vpnRedirectArmed = PendingCustomGameJoinAddressOverrideState.Arm(serverName, serverAddress, serverPort, advertisedHostAddress);
                }

                ModLogger.Info(
                    "LobbyRequestJoinDiagnosticsPatch: selected custom game join target. " +
                    "serverId=" + selectedServerId +
                    " serverName=" + serverName +
                    " hostName=" + hostName +
                    " address=" + serverAddress +
                    " port=" + serverPort +
                    " map=" + map +
                    " uniqueMapId=" + uniqueMapId +
                    " isOfficial=" + isOfficial +
                    " joinAsAdmin=" + isJoinAsAdmin +
                    " passwordSet=" + (!string.IsNullOrWhiteSpace(password)) +
                    " vpnRedirectArmed=" + vpnRedirectArmed +
                    " advertisedHostAddress=" + (string.IsNullOrWhiteSpace(advertisedHostAddress) ? "(default)" : advertisedHostAddress) + ".");
            }
            catch (Exception ex)
            {
                ModLogger.Error("LobbyRequestJoinDiagnosticsPatch.Prefix failed.", ex);
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
                    ModLogger.Info("LobbyRequestJoinDiagnosticsPatch: TaleWorlds.MountAndBlade.Diamond not loaded, skip.");
                    return;
                }

                Type lobbyClientType = diamondAssembly.GetType("TaleWorlds.MountAndBlade.Diamond.LobbyClient");
                if (lobbyClientType == null)
                {
                    ModLogger.Info("LobbyRequestJoinDiagnosticsPatch: LobbyClient type not found.");
                    return;
                }

                MethodInfo method = lobbyClientType
                    .GetMethods(BindingFlags.Instance | BindingFlags.Public)
                    .FirstOrDefault(m => m.Name == "RequestJoinCustomGame" && m.GetParameters().Length >= 2);
                if (method == null)
                {
                    ModLogger.Info("LobbyRequestJoinDiagnosticsPatch: RequestJoinCustomGame not found.");
                    return;
                }

                MethodInfo prefix = typeof(LobbyRequestJoinDiagnosticsPatch).GetMethod(nameof(Prefix), BindingFlags.Public | BindingFlags.Static);
                harmony.Patch(method, prefix: new HarmonyMethod(prefix));
                _isApplied = true;
                ModLogger.Info("LobbyRequestJoinDiagnosticsPatch: applied to LobbyClient.RequestJoinCustomGame.");
            }
            catch (Exception ex)
            {
                ModLogger.Error("LobbyRequestJoinDiagnosticsPatch.Apply failed.", ex);
            }
        }

        private static object FindSelectedServerEntry(object lobbyClient, string selectedServerId)
        {
            object availableCustomGames = GetPropertyValue(lobbyClient, "AvailableCustomGames");
            object entries = GetPropertyValue(availableCustomGames, "CustomGameServerInfos");
            if (!(entries is IEnumerable enumerable))
                return null;

            foreach (object entry in enumerable)
            {
                string entryId = GetPropertyValue(entry, "Id")?.ToString() ?? string.Empty;
                if (string.Equals(entryId, selectedServerId, StringComparison.OrdinalIgnoreCase))
                    return entry;
            }

            return null;
        }

        private static object GetPropertyValue(object target, string propertyName)
        {
            if (target == null)
                return null;

            PropertyInfo property = target.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return property?.GetValue(target);
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
