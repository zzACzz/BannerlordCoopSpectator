using System; // Type, Reflection
using System.Linq; // FirstOrDefault
using System.Reflection; // MethodInfo, BindingFlags
using HarmonyLib; // Harmony, HarmonyMethod
using TaleWorlds.Core; // InformationMessage
using TaleWorlds.Library; // InformationManager
using CoopSpectator.Infrastructure; // ModLogger

namespace CoopSpectator.Patches
{
    /// <summary>
    /// Для Join через Custom Server List на одній машині: замінює публічний IP сервера на 127.0.0.1.
    /// Ціль — LobbyGameStateCustomGameClient.StartMultiplayer (викликається при Join).
    /// Патч застосовується через reflection, щоб не вимагати референсу на TaleWorlds.MountAndBlade.Lobby при збірці.
    /// </summary>
    public static class LobbyCustomGameLocalJoinPatch
    {
        /// <summary>Публічний IP, який підставляємо на 127.0.0.1 при тесті на локалці.</summary>
        private const string PublicIpToReplace = "85.238.97.249";

        /// <summary>Prefix-метод для Harmony: якщо serverAddress — наш публічний IP, міняємо на 127.0.0.1.</summary>
        public static void Prefix(
            ref string serverAddress,
            ref int port,
            ref int sessionKey,
            ref int peerIndex)
        {
            if (string.IsNullOrEmpty(serverAddress))
                return;
            if (serverAddress != PublicIpToReplace && !serverAddress.StartsWith("85.238.97"))
                return;

            string was = serverAddress;
            serverAddress = "127.0.0.1";
            ModLogger.Info("LobbyCustomGameLocalJoinPatch: " + was + " -> 127.0.0.1");
            InformationManager.DisplayMessage(new InformationMessage("Localhost patch: " + was + " → 127.0.0.1"));
        }

        /// <summary>
        /// Застосовує патч на LobbyGameStateCustomGameClient.StartMultiplayer через reflection
        /// (збірка Lobby може бути ще не завантажена на момент PatchAll, тому викликати після початку гри або під час завантаження лобі).
        /// </summary>
        public static void Apply(Harmony harmony)
        {
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
                ModLogger.Info("LobbyCustomGameLocalJoinPatch: applied to LobbyGameStateCustomGameClient.StartMultiplayer.");
            }
            catch (Exception ex)
            {
                ModLogger.Error("LobbyCustomGameLocalJoinPatch.Apply failed.", ex);
            }
        }
    }
}
