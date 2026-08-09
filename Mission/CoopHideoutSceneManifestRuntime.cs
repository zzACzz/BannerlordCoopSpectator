using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CoopSpectator.Infrastructure;
using CoopSpectator.Infrastructure.Hideout;

namespace CoopSpectator.MissionBehaviors
{
    internal static class CoopHideoutSceneManifestRuntime
    {
        private static readonly object Sync = new object();
        private static readonly Dictionary<string, CoopHideoutSceneManifest> Cache =
            new Dictionary<string, CoopHideoutSceneManifest>(StringComparer.OrdinalIgnoreCase);

        internal static bool TryResolve(
            string sceneName,
            out CoopHideoutSceneManifest manifest,
            out string diagnostics)
        {
            manifest = null;
            diagnostics = null;
            if (!CoopHideoutBossPhaseContract.TryNormalizeDayHideoutSceneName(
                    sceneName,
                    out string normalizedSceneName))
            {
                diagnostics = "unsupported-hideout-scene";
                return false;
            }

            lock (Sync)
            {
                if (Cache.TryGetValue(normalizedSceneName, out manifest))
                {
                    diagnostics = "cached path=" + manifest.SourcePath;
                    return true;
                }
            }

            var attemptedPaths = new List<string>();
            foreach (string moduleId in new[] { "SandBox", "CoopSpectatorDedicated", "CoopSpectator" })
            {
                string moduleDataDirectory = ModulePathHelper.GetSiblingModuleDataDirectory(moduleId);
                string moduleRoot = string.IsNullOrWhiteSpace(moduleDataDirectory)
                    ? null
                    : Directory.GetParent(moduleDataDirectory)?.FullName;
                if (string.IsNullOrWhiteSpace(moduleRoot))
                    continue;

                string candidatePath = Path.Combine(
                    moduleRoot,
                    "SceneObj",
                    normalizedSceneName,
                    "scene.xscene");
                if (attemptedPaths.Contains(candidatePath, StringComparer.OrdinalIgnoreCase))
                    continue;

                attemptedPaths.Add(candidatePath);
                if (!File.Exists(candidatePath))
                    continue;

                if (!CoopHideoutSceneManifest.TryLoad(
                        candidatePath,
                        normalizedSceneName,
                        out manifest,
                        out string loadDiagnostics))
                {
                    diagnostics = loadDiagnostics + " path=" + candidatePath;
                    continue;
                }

                lock (Sync)
                    Cache[normalizedSceneName] = manifest;
                diagnostics = loadDiagnostics + " path=" + candidatePath;
                return true;
            }

            diagnostics = attemptedPaths.Count == 0
                ? "scene-manifest-module-roots-unresolved"
                : "scene-manifest-file-not-found paths=" + string.Join("|", attemptedPaths);
            return false;
        }
    }
}
