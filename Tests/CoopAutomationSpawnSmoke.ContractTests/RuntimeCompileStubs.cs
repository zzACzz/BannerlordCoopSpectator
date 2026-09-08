// Native-free stand-ins used only by the local file-bridge contract tests.
namespace TaleWorlds.MountAndBlade
{
    public static class GameNetwork
    {
        public static bool IsClient { get; set; }
        public static bool IsServer { get; set; }
    }
}
namespace CoopSpectator.Infrastructure
{
    internal static class ModLogger
    {
        public static void Info(string message) { }
    }
}
