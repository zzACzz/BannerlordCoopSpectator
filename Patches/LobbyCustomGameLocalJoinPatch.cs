using System; // Type, Reflection
using System.Linq; // FirstOrDefault
using System.Reflection; // MethodInfo, BindingFlags
using HarmonyLib; // Harmony, HarmonyMethod
using CoopSpectator.Infrastructure; // ModLogger

namespace CoopSpectator.Patches
{
    /// <summary>
    /// Reflection patch for lobby join path. It no longer rewrites every advertised address
    /// to localhost; it only consumes the one-shot host self-join redirect armed from the
    /// native join result message.
    /// </summary>
    public static class LobbyCustomGameLocalJoinPatch
    {
        private static bool _isApplied;

        public static void Prefix(
            ref string serverAddress,
            ref int port,
            ref int sessionKey,
            ref int peerIndex)
        {
            HostSelfJoinRedirectState.TryConsumeLoopbackRewrite(ref serverAddress, port, "LobbyGameStateCustomGameClient.StartMultiplayer");
        }

        /// <summary>
        /// Застосовує патч на LobbyGameStateCustomGameClient.StartMultiplayer через reflection
        /// (збірка Lobby може бути ще не завантажена на момент PatchAll, тому викликати після початку гри або під час завантаження лобі).
        /// </summary>
        public static void Apply(Harmony harmony)
        {
            if (_isApplied)
                return;

            try
            {
                var lobbyAssembly = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name == "TaleWorlds.MountAndBlade.Lobby");
                if (lobbyAssembly == null)
                {
                    ModLogger.Info("LobbyCustomGameLocalJoinPatch: TaleWorlds.MountAndBlade.Lobby not loaded, skip.");
                    return;
                }

                var type = lobbyAssembly.GetType("TaleWorlds.MountAndBlade.Lobby.LobbyGameStateCustomGameClient");
                if (type == null)
                {
                    ModLogger.Info("LobbyCustomGameLocalJoinPatch: LobbyGameStateCustomGameClient type not found.");
                    return;
                }

                var method = type.GetMethod("StartMultiplayer", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                    null, new Type[] { typeof(string), typeof(int), typeof(int), typeof(int) }, null);
                if (method == null)
                {
                    ModLogger.Info("LobbyCustomGameLocalJoinPatch: StartMultiplayer(string,int,int,int) not found.");
                    return;
                }

                var prefix = typeof(LobbyCustomGameLocalJoinPatch).GetMethod("Prefix", BindingFlags.Public | BindingFlags.Static);
                harmony.Patch(method, prefix: new HarmonyMethod(prefix));
                _isApplied = true;
                ModLogger.Info("LobbyCustomGameLocalJoinPatch: applied to LobbyGameStateCustomGameClient.StartMultiplayer.");
            }
            catch (Exception ex)
            {
                ModLogger.Error("LobbyCustomGameLocalJoinPatch.Apply failed.", ex);
            }
        }
    }
}
