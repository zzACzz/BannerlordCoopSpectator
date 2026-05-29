using System;
using System.Reflection;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.Infrastructure
{
    internal static class ListedShellLobbyClientInteropRuntime
    {
        private const int HostedServerCustomGameState = 14;
        private const int JoinedCustomGameState = 16;

        public static object ResolveLobbyClient()
        {
            try
            {
                return typeof(NetworkMain)
                    .GetProperty("GameClient", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?
                    .GetValue(null);
            }
            catch
            {
                return null;
            }
        }

        public static void HandleQuitMissionInterop(
            object lobbyClient,
            bool isServer,
            bool isEnding,
            bool isServerEndedBeforeClientLoaded)
        {
            if (lobbyClient == null || isEnding || !IsLobbyClientLoggedIn(lobbyClient))
                return;

            int currentState = ResolveLobbyClientState(lobbyClient);
            if (isServer)
            {
                if (currentState == HostedServerCustomGameState)
                    TryInvokeLobbyClientMethod(lobbyClient, "EndCustomGame");

                return;
            }

            if (!isServerEndedBeforeClientLoaded && currentState == JoinedCustomGameState)
                TryInvokeLobbyClientMethod(lobbyClient, "QuitFromCustomGame");
        }

        public static void TrySetCriticalState(object lobbyClient, bool isCritical)
        {
            try
            {
                lobbyClient?.GetType()
                    .GetProperty("IsInCriticalState", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?
                    .SetValue(lobbyClient, isCritical);
            }
            catch
            {
            }
        }

        private static bool IsLobbyClientLoggedIn(object lobbyClient)
        {
            try
            {
                object loggedInValue = lobbyClient?.GetType()
                    .GetProperty("LoggedIn", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?
                    .GetValue(lobbyClient);
                return loggedInValue is bool loggedIn && loggedIn;
            }
            catch
            {
                return false;
            }
        }

        private static int ResolveLobbyClientState(object lobbyClient)
        {
            try
            {
                object currentState = lobbyClient?.GetType()
                    .GetProperty("CurrentState", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?
                    .GetValue(lobbyClient);
                return currentState != null ? Convert.ToInt32(currentState) : -1;
            }
            catch
            {
                return -1;
            }
        }

        private static void TryInvokeLobbyClientMethod(object lobbyClient, string methodName)
        {
            try
            {
                lobbyClient?.GetType()
                    .GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?
                    .Invoke(lobbyClient, null);
            }
            catch
            {
            }
        }
    }
}
