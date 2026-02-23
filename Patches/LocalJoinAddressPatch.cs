using HarmonyLib; // HarmonyPatch, HarmonyArgument
using TaleWorlds.MountAndBlade; // GameNetwork
using CoopSpectator.Infrastructure; // ModLogger

namespace CoopSpectator.Patches
{
    /// <summary>
    /// Для тесту на одній машині: клієнт з Custom Server List отримує публічний IP сервера (85.x.x.x),
    /// підключення на той самий комп часто не працює через NAT (hairpinning). Патч підставляє 127.0.0.1
    /// замість адреси сервера, щоб Join йшов на localhost. Вмикати лише при тесті "хост = клієнт на одному ПК".
    /// </summary>
    internal static class LocalJoinAddressPatch
    {
        /// <summary>true = при Join через Custom Server List підставляти 127.0.0.1 замість IP сервера (для тесту на одній машині).</summary>
        public const bool EnableLocalJoinRedirect = true;

        [HarmonyPatch(typeof(GameNetwork))]
        [HarmonyPatch("StartMultiplayerOnClient")]
        private static class StartMultiplayerOnClientPatch
        {
            /// <summary>Prefix: замінюємо адресу сервера на 127.0.0.1, якщо увімкнено перенаправлення. [HarmonyArgument(0)] — перший аргумент методу.</summary>
            public static void Prefix([HarmonyArgument(0)] ref string serverAddress)
            {
                if (!EnableLocalJoinRedirect || string.IsNullOrEmpty(serverAddress))
                    return;
                string was = serverAddress;
                serverAddress = "127.0.0.1";
                ModLogger.Info("LocalJoinAddressPatch: redirecting join address \"" + was + "\" -> 127.0.0.1");
            }
        }
    }
}
