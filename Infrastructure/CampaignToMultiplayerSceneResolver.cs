using System;
using System.Linq;
using TaleWorlds.CampaignSystem;

namespace CoopSpectator.Infrastructure
{
    public sealed class MultiplayerSceneResolution
    {
        public string CampaignBattleScene { get; set; }
        public string RuntimeScene { get; set; }
        public string RuntimeGameType { get; set; }
        public string Source { get; set; }
        public string Terrain { get; set; }
        public string ForestDensity { get; set; }
        public bool IsNaval { get; set; }
    }

    public static class CampaignToMultiplayerSceneResolver
    {
        private const string DefaultRuntimeGameType = "Battle";
        private const string DefaultRuntimeScene = "mp_battle_map_001";

        public static MultiplayerSceneResolution Resolve(string campaignBattleScene)
        {
            var resolution = new MultiplayerSceneResolution
            {
                CampaignBattleScene = campaignBattleScene ?? string.Empty,
                RuntimeScene = DefaultRuntimeScene,
                RuntimeGameType = DefaultRuntimeGameType,
                Source = "default-fallback",
                Terrain = "Unknown",
                ForestDensity = "Unknown",
                IsNaval = false
            };

            if (string.IsNullOrWhiteSpace(campaignBattleScene))
                return resolution;

            try
            {
                GameSceneDataManager manager = GameSceneDataManager.Instance;
                SingleplayerBattleSceneData? sceneData = manager?.SingleplayerBattleScenes?
                    .FirstOrDefault(scene =>
                        string.Equals(scene.SceneID, campaignBattleScene, StringComparison.OrdinalIgnoreCase));

                if (!sceneData.HasValue || string.IsNullOrWhiteSpace(sceneData.Value.SceneID))
                {
                    resolution.Source = "metadata-missing-fallback";
                    return resolution;
                }

                SingleplayerBattleSceneData resolvedSceneData = sceneData.Value;

                resolution.Terrain = resolvedSceneData.Terrain.ToString();
                resolution.ForestDensity = resolvedSceneData.ForestDensity.ToString();
                resolution.IsNaval = resolvedSceneData.IsNaval;

                if (resolvedSceneData.IsNaval)
                {
                    resolution.RuntimeScene = "mp_battle_map_003";
                    resolution.Source = "naval-fallback";
                    return resolution;
                }

                switch (resolvedSceneData.Terrain.ToString())
                {
                    case "Desert":
                        resolution.RuntimeScene = "mp_battle_map_003";
                        resolution.Source = "terrain-desert";
                        break;

                    case "Steppe":
                        resolution.RuntimeScene = "mp_battle_map_002";
                        resolution.Source = "terrain-steppe";
                        break;

                    case "Swamp":
                        resolution.RuntimeScene = "mp_battle_map_002";
                        resolution.Source = "terrain-swamp";
                        break;

                    case "Plain":
                        if (string.Equals(resolvedSceneData.ForestDensity.ToString(), "High", StringComparison.OrdinalIgnoreCase))
                        {
                            resolution.RuntimeScene = "mp_battle_map_002";
                            resolution.Source = "terrain-plain-forest-high";
                        }
                        else
                        {
                            resolution.RuntimeScene = "mp_battle_map_001";
                            resolution.Source = "terrain-plain";
                        }
                        break;

                    default:
                        resolution.RuntimeScene = DefaultRuntimeScene;
                        resolution.Source = "terrain-default";
                        break;
                }
            }
            catch (Exception ex)
            {
                resolution.Source = "exception-fallback:" + ex.GetType().Name;
            }

            return resolution;
        }
    }
}
