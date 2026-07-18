using System;
using System.Reflection;
using CoopSpectator.Infrastructure;
using HarmonyLib;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.Patches
{
    public static class SiegeMissionGameTypeAliasPatch
    {
        private const string SiegeMissionWithDeploymentShell = "SiegeMissionWithDeployment";
        private const string BattleGameTypeAlias = "Battle";

        private static readonly string[] TextIdsUsingOfficialGameTypeVariation =
        {
            "str_multiplayer_game_type",
            "str_multiplayer_official_game_type_name",
            "str_multiplayer_official_game_type_description",
            "str_multiplayer_official_game_type_objective_info",
            "str_multiplayer_official_game_type_troops_info",
            "str_multiplayer_official_game_type_explainer"
        };

        public static void Apply(Harmony harmony)
        {
            try
            {
                PatchGetStrValue(harmony);
                PatchFindText(harmony);
                ModLogger.Info("SiegeMissionGameTypeAliasPatch: applied client UI game-type alias patches.");
            }
            catch (Exception ex)
            {
                ModLogger.Error("SiegeMissionGameTypeAliasPatch.Apply failed.", ex);
            }
        }

        private static void PatchGetStrValue(Harmony harmony)
        {
            MethodInfo target = AccessTools.Method(
                typeof(MultiplayerOptionsExtensions),
                nameof(MultiplayerOptionsExtensions.GetStrValue),
                new[] { typeof(MultiplayerOptions.OptionType), typeof(MultiplayerOptions.MultiplayerOptionsAccessMode) });
            MethodInfo postfix = typeof(SiegeMissionGameTypeAliasPatch).GetMethod(
                nameof(MultiplayerOptionsExtensions_GetStrValue_Postfix),
                BindingFlags.Static | BindingFlags.NonPublic);
            if (target == null || postfix == null)
            {
                ModLogger.Info("SiegeMissionGameTypeAliasPatch: MultiplayerOptionsExtensions.GetStrValue patch skipped.");
                return;
            }

            harmony.Patch(target, postfix: new HarmonyMethod(postfix));
        }

        private static void PatchFindText(Harmony harmony)
        {
            MethodInfo target = AccessTools.Method(
                typeof(GameTexts),
                nameof(GameTexts.FindText),
                new[] { typeof(string), typeof(string) });
            MethodInfo prefix = typeof(SiegeMissionGameTypeAliasPatch).GetMethod(
                nameof(GameTexts_FindText_Prefix),
                BindingFlags.Static | BindingFlags.NonPublic);
            if (target == null || prefix == null)
            {
                ModLogger.Info("SiegeMissionGameTypeAliasPatch: GameTexts.FindText patch skipped.");
                return;
            }

            harmony.Patch(target, prefix: new HarmonyMethod(prefix));
        }

        private static void MultiplayerOptionsExtensions_GetStrValue_Postfix(
            MultiplayerOptions.OptionType optionType,
            ref string __result)
        {
            if (optionType != MultiplayerOptions.OptionType.GameType &&
                optionType != MultiplayerOptions.OptionType.PremadeMatchGameMode)
            {
                return;
            }

            if (!ShouldAliasSiegeMissionGameType(__result))
                return;

            __result = BattleGameTypeAlias;
        }

        private static void GameTexts_FindText_Prefix(string id, ref string variation)
        {
            if (!UsesOfficialGameTypeVariation(id))
                return;

            if (!ShouldAliasSiegeMissionGameType(variation))
                return;

            variation = BattleGameTypeAlias;
        }

        private static bool UsesOfficialGameTypeVariation(string textId)
        {
            if (string.IsNullOrWhiteSpace(textId))
                return false;

            for (int i = 0; i < TextIdsUsingOfficialGameTypeVariation.Length; i++)
            {
                if (string.Equals(TextIdsUsingOfficialGameTypeVariation[i], textId, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        private static bool ShouldAliasSiegeMissionGameType(string value)
        {
            if (!GameNetwork.IsClient || GameNetwork.IsDedicatedServer)
                return false;

            if (!string.Equals(value, SiegeMissionWithDeploymentShell, StringComparison.Ordinal))
                return false;

            return ExactCampaignSiegeAssaultWithDeploymentRuntime.IsExactSiegeWithDeploymentScenario(
                BattleSnapshotRuntimeState.GetScenarioContext());
        }
    }
}
