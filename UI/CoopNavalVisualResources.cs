using System;
using System.IO;
using System.Linq;
using CoopSpectator.Infrastructure;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.UI
{
    internal static class CoopNavalVisualResources
    {
        private static readonly CoopNavalTextureAdmission TextureAdmission = new CoopNavalTextureAdmission();

        internal static void EnsureTextureTilesetLoaded()
        {
            object serverType = Module.CurrentModule?.StartupInfo?.DedicatedServerType;
            bool dedicated = serverType != null && !string.Equals(serverType.ToString(), "None", StringComparison.OrdinalIgnoreCase);
            if (TextureAdmission.Ensure(dedicated, CoopCampaignContentRuntime.IsCampaign,
                ModulePathHelper.GetActiveModulePaths().Select(module => module.Key),
                ActivateAndRebind))
                ModLogger.Info("Naval visual texture tileset activated: " + CoopCampaignContentProfile.NavalAdapterId);
        }

        private static void ActivateAndRebind(string moduleId)
        {
            string folder = ModulePathHelper.GetActiveModulePaths()
                .Single(module => string.Equals(module.Key, moduleId, StringComparison.OrdinalIgnoreCase)).Value;
            var names = CoopNavalMaterialCatalog.ReadNames(Path.Combine(folder, "TileSets", "ValidMaterials.xml"));
            TaleWorlds.Engine.Utilities.LoadVirtualTextureTileset(moduleId);
            int rebound = 0;
            foreach (string name in names)
            {
                var material = TaleWorlds.Engine.Material.GetFromResource(name);
                if (material == null) continue;
                var texture = material.GetTexture(TaleWorlds.Engine.Material.MBTextureType.DiffuseMap);
                if (texture == null) continue;
                // The public setter recalculates the tileset binding even when
                // the texture is unchanged. Existing source templates otherwise
                // retain Native's tileset; stock team-color copies recalculate it.
                material.SetTexture(TaleWorlds.Engine.Material.MBTextureType.DiffuseMap, texture);
                rebound++;
            }
            ModLogger.Info("Naval material texture bindings refreshed: " + rebound + "/" + names.Count);
        }
    }
}
