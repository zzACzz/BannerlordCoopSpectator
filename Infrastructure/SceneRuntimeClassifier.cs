using System;

namespace CoopSpectator.Infrastructure
{
    /// <summary>
    /// Shared scene-name classification helpers that are safe to use from both
    /// client/campaign and dedicated-only builds.
    /// </summary>
    public static class SceneRuntimeClassifier
    {
        private const string OfficialBattleMapScenePrefix = "mp_battle_map_";
        private const string CampaignBattleScenePrefix = "battle_terrain_";

        public static bool IsOfficialMultiplayerBattleScene(string sceneName)
        {
            return !string.IsNullOrWhiteSpace(sceneName)
                && sceneName.StartsWith(OfficialBattleMapScenePrefix, StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsCampaignBattleScene(string sceneName)
        {
            return !string.IsNullOrWhiteSpace(sceneName)
                && sceneName.StartsWith(CampaignBattleScenePrefix, StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsSceneAwareBattleRuntimeScene(string sceneName)
        {
            return IsOfficialMultiplayerBattleScene(sceneName)
                || IsCampaignBattleScene(sceneName);
        }
    }
}
