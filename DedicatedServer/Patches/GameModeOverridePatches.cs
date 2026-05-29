using System.Reflection;
using CoopSpectator.GameMode;
using HarmonyLib;
using CoopSpectator.Infrastructure;

namespace CoopSpectator.Patches
{
    /// <summary>
    /// Overrides official multiplayer runtime lookup/startup so Battle and listed TeamDeathmatch
    /// can boot coop-owned entry points without changing their public ids.
    /// </summary>
    public static class GameModeOverridePatches
    {
        private static object _battleOverride;
        private static object _teamDeathmatchOverride;

        public static void SetBattleOverride(object gameMode)
        {
            _battleOverride = gameMode;
        }

        public static void SetTeamDeathmatchOverride(object gameMode)
        {
            _teamDeathmatchOverride = gameMode;
        }

        public static void Apply(Harmony harmony)
        {
            try
            {
                MethodInfo getMultiplayerGameMode = AccessTools.Method(typeof(TaleWorlds.MountAndBlade.Module), "GetMultiplayerGameMode", new[] { typeof(string) });
                MethodInfo startMultiplayerGame = AccessTools.Method(typeof(TaleWorlds.MountAndBlade.Module), "StartMultiplayerGame", new[] { typeof(string), typeof(string) });
                if (getMultiplayerGameMode == null || startMultiplayerGame == null)
                {
                    ModLogger.Info("GameModeOverridePatches: multiplayer runtime override targets not found. Skip.");
                    return;
                }

                MethodInfo postfix = typeof(GameModeOverridePatches).GetMethod(nameof(GetMultiplayerGameMode_Postfix), BindingFlags.Public | BindingFlags.Static);
                MethodInfo prefix = typeof(GameModeOverridePatches).GetMethod(nameof(StartMultiplayerGame_Prefix), BindingFlags.Public | BindingFlags.Static);
                if (postfix == null || prefix == null)
                {
                    ModLogger.Info("GameModeOverridePatches: override patch methods not found. Skip.");
                    return;
                }

                harmony.Patch(getMultiplayerGameMode, postfix: new HarmonyMethod(postfix));
                harmony.Patch(startMultiplayerGame, prefix: new HarmonyMethod(prefix));
                ModLogger.Info("GameModeOverridePatches: GetMultiplayerGameMode postfix and StartMultiplayerGame prefix applied.");
            }
            catch (System.Exception ex)
            {
                ModLogger.Info("[HarmonyFallback] GameModeOverridePatches.Apply failed. patchName=GetMultiplayerGameMode_Postfix/StartMultiplayerGame_Prefix originalTargets=Module.GetMultiplayerGameMode(string), Module.StartMultiplayerGame(string,string). skipped intentionally, fallback active. " + ex.GetType().FullName + ": " + ex.Message);
                ModLogger.Error("GameModeOverridePatches.Apply failed.", ex);
            }
        }

        /// <summary>After the vanilla lookup, replace Battle with the coop runtime when armed.</summary>
        public static void GetMultiplayerGameMode_Postfix(string gameType, ref object __result)
        {
            if (string.IsNullOrEmpty(gameType))
                return;

            if (string.Equals(gameType, CoopGameModeIds.OfficialBattle, System.StringComparison.Ordinal) && _battleOverride != null)
            {
                __result = _battleOverride;
                return;
            }

            if (string.Equals(gameType, CoopGameModeIds.OfficialTeamDeathmatch, System.StringComparison.Ordinal) && _teamDeathmatchOverride != null)
                __result = _teamDeathmatchOverride;
        }

        /// <summary>
        /// The actual listed/custom-game startup path goes through Module.StartMultiplayerGame,
        /// not just GetMultiplayerGameMode lookup, so own the TeamDeathmatch entry point here.
        /// </summary>
        public static bool StartMultiplayerGame_Prefix(string multiplayerGameType, string scene, ref bool __result)
        {
            if (!string.Equals(multiplayerGameType, CoopGameModeIds.OfficialTeamDeathmatch, System.StringComparison.Ordinal) ||
                _teamDeathmatchOverride == null)
                return true;

            InvokeStartMultiplayerGame(_teamDeathmatchOverride, scene);
            __result = true;
            ModLogger.Info(
                "GameModeOverridePatches: rerouted Module.StartMultiplayerGame for official TeamDeathmatch to explicit listed shell mode. " +
                "Scene=" + (scene ?? string.Empty) +
                " OverrideType=" + _teamDeathmatchOverride.GetType().FullName + ".");
            return false;
        }

        private static void InvokeStartMultiplayerGame(object gameMode, string scene)
        {
            if (gameMode == null)
                return;

            if (gameMode is MissionMultiplayerListedShellMode listedShellMode)
            {
                listedShellMode.StartMultiplayerGame(scene);
                return;
            }

            if (gameMode is MissionMultiplayerCoopBattleMode coopBattleMode)
            {
                coopBattleMode.StartMultiplayerGame(scene);
                return;
            }

            MethodInfo startMethod = AccessTools.Method(gameMode.GetType(), "StartMultiplayerGame", new[] { typeof(string) });
            startMethod?.Invoke(gameMode, new object[] { scene });
        }
    }
}
