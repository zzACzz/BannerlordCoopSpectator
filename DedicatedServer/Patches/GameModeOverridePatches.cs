// Harmony-патч: підміна GetMultiplayerGameMode("TeamDeathmatch") на наш режим (3+3 спавн).
// Ванільний Multiplayer реєструє "TeamDeathmatch" раніше, тому lookup повертає його — підмінюємо результат тут.
using System.Reflection;
using HarmonyLib;
using CoopSpectator.Infrastructure;

namespace CoopSpectator.Patches
{
    /// <summary>
    /// Підміняє результат GetMultiplayerGameMode("TeamDeathmatch") на наш MissionMultiplayerTdmCloneMode,
    /// щоб при GameType TeamDeathmatch у конфігу дедика запускалась наша місія зі спавном 3+3.
    /// </summary>
    public static class GameModeOverridePatches
    {
        /// <summary>Наш режим, який повертаємо замість ванільного TDM при запиті "TeamDeathmatch".</summary>
        private static object _teamDeathmatchOverride;

        /// <summary>Встановити режим для підміни (викликати з SubModule перед/після AddMultiplayerGameMode).</summary>
        public static void SetTeamDeathmatchOverride(object gameMode)
        {
            _teamDeathmatchOverride = gameMode;
        }

        public static void Apply(Harmony harmony)
        {
            try
            {
                // Module — TaleWorlds.MountAndBlade.Module, метод GetMultiplayerGameMode(string).
                MethodInfo target = AccessTools.Method(typeof(TaleWorlds.MountAndBlade.Module), "GetMultiplayerGameMode", new[] { typeof(string) });
                if (target == null)
                {
                    ModLogger.Info("GameModeOverridePatches: GetMultiplayerGameMode(string) not found. Skip.");
                    return;
                }

                MethodInfo postfix = typeof(GameModeOverridePatches).GetMethod(nameof(GetMultiplayerGameMode_Postfix), BindingFlags.Public | BindingFlags.Static);
                if (postfix == null)
                {
                    ModLogger.Info("GameModeOverridePatches: postfix method not found. Skip.");
                    return;
                }

                harmony.Patch(target, postfix: new HarmonyMethod(postfix));
                ModLogger.Info("GameModeOverridePatches: GetMultiplayerGameMode postfix applied (TeamDeathmatch -> TdmClone 3+3).");
            }
            catch (System.Exception ex)
            {
                ModLogger.Error("GameModeOverridePatches.Apply failed.", ex);
            }
        }

        /// <summary>Після виклику оригіналу: якщо запитували "TeamDeathmatch" і є наш override — повертаємо його.</summary>
        public static void GetMultiplayerGameMode_Postfix(string gameType, ref object __result)
        {
            if (_teamDeathmatchOverride == null) return;
            if (string.IsNullOrEmpty(gameType)) return;
            if (!string.Equals(gameType, CoopGameModeIds.OfficialTeamDeathmatch, System.StringComparison.Ordinal)) return;

            __result = _teamDeathmatchOverride;
        }
    }
}
