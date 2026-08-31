using System;
using System.Reflection;

namespace CoopSpectator.Infrastructure
{
    public static class ModLogger
    {
        public static void Info(string message)
        {
        }

        public static void Error(string message, Exception exception)
        {
        }
    }

    internal static class HostSelfJoinRedirectState
    {
        public static bool IsPersistedHostSessionActive(string serverName, int port)
        {
            return true;
        }

        public static bool TryConsumeLoopbackRewrite(ref string serverAddress, int port, string source)
        {
            return false;
        }
    }
}

namespace HarmonyLib
{
    public sealed class Harmony
    {
        public void Patch(MethodBase original, HarmonyMethod prefix = null)
        {
        }
    }

    public sealed class HarmonyMethod
    {
        public HarmonyMethod(MethodInfo method)
        {
        }
    }
}

namespace TaleWorlds.Library
{
    public static class CommandLineFunctionality
    {
        [AttributeUsage(AttributeTargets.Method)]
        public sealed class CommandLineArgumentFunctionAttribute : Attribute
        {
            public CommandLineArgumentFunctionAttribute(string name, string group)
            {
            }
        }
    }
}

namespace TaleWorlds.MountAndBlade
{
    public static class GameNetwork
    {
        public static bool IsClient => false;
        public static bool IsSessionActive => false;
    }
}
