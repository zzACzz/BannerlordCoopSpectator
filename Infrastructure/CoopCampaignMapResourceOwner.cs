using System;
using System.Collections.Generic;
using System.IO;

namespace CoopSpectator.Infrastructure
{
    internal static class CoopCampaignMapResourceOwner
    {
        // Match SandBox.MapScene.GetMainMapModule: last active module whose
        // actual FolderPath contains SceneObj/Main_map/scene.xscene. Native
        // per-module scene-name enumeration is not equivalent for platform DLC.
        internal static string Resolve(IEnumerable<KeyValuePair<string, string>> activeModules)
        {
            if (activeModules == null) throw new IOException("Active module metadata is not ready.");
            string owner = null;
            foreach (var module in activeModules)
            {
                if (string.IsNullOrWhiteSpace(module.Key) || string.IsNullOrWhiteSpace(module.Value))
                    throw new IOException("Active module identity or folder is unavailable.");
                if (File.Exists(Path.Combine(module.Value, "SceneObj", "Main_map", "scene.xscene")))
                    owner = module.Key;
            }
            return owner;
        }
    }
}
