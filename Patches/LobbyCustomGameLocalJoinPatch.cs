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
        private static readonly string[] CandidateAssemblyNames =
        {
            "TaleWorlds.MountAndBlade.Multiplayer",
            "TaleWorlds.MountAndBlade.Lobby"
        };

        private static readonly string[] CandidateTypeNames =
        {
            "TaleWorlds.MountAndBlade.LobbyGameStateCustomGameClient",
            "TaleWorlds.MountAndBlade.Lobby.LobbyGameStateCustomGameClient"
        };

        public static void Prefix(
            ref string serverAddress,
            ref int port,
            ref int sessionKey,
            ref int peerIndex)
        {
            string originalAddress = serverAddress;
            bool consumed = HostSelfJoinRedirectState.TryConsumeLoopbackRewrite(ref serverAddress, port, "LobbyGameStateCustomGameClient.StartMultiplayer");
            ModLogger.Info(
                "LobbyCustomGameLocalJoinPatch: lobby join handoff. " +
                "originalAddress=" + (originalAddress ?? string.Empty) +
                " finalAddress=" + (serverAddress ?? string.Empty) +
                " port=" + port +
                " sessionKey=" + sessionKey +
                " peerIndex=" + peerIndex +
                " selfJoinRedirect=" + consumed + ".");
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
                Assembly lobbyAssembly = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => CandidateAssemblyNames.Contains(a.GetName().Name));
                if (lobbyAssembly == null)
                {
                    ModLogger.Info("LobbyCustomGameLocalJoinPatch: multiplayer lobby assembly not loaded, skip.");
                    return;
                }

                Type type = null;
                for (int i = 0; i < CandidateTypeNames.Length && type == null; i++)
                    type = lobbyAssembly.GetType(CandidateTypeNames[i]);

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
                ModLogger.Info(
                    "LobbyCustomGameLocalJoinPatch: applied to " +
                    type.FullName +
                    " in assembly " +
                    lobbyAssembly.GetName().Name +
                    ".");
            }
            catch (Exception ex)
            {
                ModLogger.Error("LobbyCustomGameLocalJoinPatch.Apply failed.", ex);
            }
        }
    }
}
