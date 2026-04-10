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

                PropertyInfo successProperty = __0.GetType().GetProperty("Success", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (successProperty == null || !(successProperty.GetValue(__0) is bool success) || !success)
                    return;

                object joinGameData = GetPropertyValue(__0, "JoinGameData");
                object gameServerProperties = GetPropertyValue(joinGameData, "GameServerProperties");
                if (gameServerProperties == null)
                    return;

                string serverName = GetPropertyValue(gameServerProperties, "Name") as string;
                string serverAddress = GetPropertyValue(gameServerProperties, "Address") as string;
                int serverPort = GetIntPropertyValue(gameServerProperties, "Port");
                if (serverPort <= 0)
                    return;

                HostSelfJoinRedirectState.ArmForNextJoinIfCurrentHost(serverName, serverAddress, serverPort);
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

        private static int GetIntPropertyValue(object target, string propertyName)
        {
            object value = GetPropertyValue(target, propertyName);
            return value is int intValue ? intValue : 0;
        }
    }
}
