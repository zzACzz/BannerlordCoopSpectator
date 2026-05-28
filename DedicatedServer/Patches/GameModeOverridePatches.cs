using System.Reflection;
using HarmonyLib;
using CoopSpectator.Infrastructure;

namespace CoopSpectator.Patches
{
    /// <summary>
    /// Overrides the official Battle lookup so the engine boots our CoopBattle runtime
    /// while the listed TeamDeathmatch path remains vanilla.
    /// </summary>
    public static class GameModeOverridePatches
    {
        private static object _battleOverride;

        public static void SetBattleOverride(object gameMode)
        {
            _battleOverride = gameMode;
        }

        public static void Apply(Harmony harmony)
        {
            try
            {
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
                ModLogger.Info("GameModeOverridePatches: GetMultiplayerGameMode postfix applied (Battle override active).");
            }
            catch (System.Exception ex)
            {
                ModLogger.Info("[HarmonyFallback] GameModeOverridePatches.Apply failed. patchName=GetMultiplayerGameMode_Postfix originalTarget=Module.GetMultiplayerGameMode(string). skipped intentionally, fallback active. " + ex.GetType().FullName + ": " + ex.Message);
                ModLogger.Error("GameModeOverridePatches.Apply failed.", ex);
            }
        }

        /// <summary>After the vanilla lookup, replace Battle with the coop runtime when armed.</summary>
        public static void GetMultiplayerGameMode_Postfix(string gameType, ref object __result)
        {
            if (string.IsNullOrEmpty(gameType))
                return;

            if (string.Equals(gameType, CoopGameModeIds.OfficialBattle, System.StringComparison.Ordinal) && _battleOverride != null)
                __result = _battleOverride;
        }
    }
}
