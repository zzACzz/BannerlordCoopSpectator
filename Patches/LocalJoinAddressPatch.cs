using HarmonyLib; // HarmonyPatch, HarmonyArgument
using TaleWorlds.MountAndBlade; // GameNetwork
using CoopSpectator.Infrastructure;

namespace CoopSpectator.Patches
{
    /// <summary>
    /// Host self-join workaround: one-shot localhost rewrite only after the current host
    /// explicitly joins its own dedicated from the same machine. Remote players must keep
    /// the real server address, so no global localhost rewrite is allowed here.
    /// </summary>
    internal static class LocalJoinAddressPatch
    {
        [HarmonyPatch(typeof(GameNetwork))]
        [HarmonyPatch("StartMultiplayerOnClient")]
        private static class StartMultiplayerOnClientPatch
        {
            public static void Prefix([HarmonyArgument(0)] ref string serverAddress, [HarmonyArgument(1)] int port)
            {
                HostSelfJoinRedirectState.TryConsumeLoopbackRewrite(ref serverAddress, port, "GameNetwork.StartMultiplayerOnClient");
            }
        }
    }
}
