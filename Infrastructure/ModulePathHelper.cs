using System;
using System.IO;
using System.Reflection;

namespace CoopSpectator.Infrastructure
{
    /// <summary>
    /// Resolves module and ModuleData paths without taking a hard reference on
    /// TaleWorlds.ModuleManager. This keeps the helper usable on both client
    /// and dedicated runtimes.
    /// </summary>
    public static class ModulePathHelper
    {
        private const string CurrentModuleId = "CoopSpectator";

        public static string GetModuleDataFilePath(string relativePathInModuleData)
        {
            return GetSiblingModuleDataFilePath(CurrentModuleId, relativePathInModuleData);
        }

        public static string GetSiblingModuleDataFilePath(string moduleId, string relativePathInModuleData)
        {
            string moduleRoot = TryGetModuleRootPath(moduleId);
            if (string.IsNullOrEmpty(moduleRoot))
                return null;

            return Path.Combine(moduleRoot, "ModuleData", relativePathInModuleData);
        }

        public static string GetSiblingModuleDataDirectory(string moduleId)
        {
            string moduleRoot = TryGetModuleRootPath(moduleId);
            if (string.IsNullOrEmpty(moduleRoot))
                return null;

            return Path.Combine(moduleRoot, "ModuleData");
        }

        private static string TryGetModuleRootPath(string moduleId)
        {
            if (string.IsNullOrWhiteSpace(moduleId))
                return null;

            try
            {
                Type moduleHelperType = Type.GetType("TaleWorlds.ModuleManager.ModuleHelper, TaleWorlds.MountAndBlade", throwOnError: false)
                    ?? Type.GetType("TaleWorlds.ModuleManager.ModuleHelper, TaleWorlds.ModuleManager", throwOnError: false);
                if (moduleHelperType != null)
                {
                    MethodInfo getPath = moduleHelperType.GetMethod(
                        "GetModuleFullPath",
                        BindingFlags.Public | BindingFlags.Static,
                        null,
                        new[] { typeof(string) },
                        null);
                    if (getPath != null)
                    {
                        string path = getPath.Invoke(null, new object[] { moduleId }) as string;
                        if (!string.IsNullOrEmpty(path))
                            return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    }
                }
            }
            catch (Exception ex)
            {
                ModLogger.Info(
                    "ModulePathHelper: ModuleHelper.GetModuleFullPath failed for " +
                    moduleId + ": " + ex.Message);
            }

            try
            {
                string binDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                if (!string.IsNullOrEmpty(binDir))
                {
                    string currentModuleRoot = Path.GetFullPath(Path.Combine(binDir, "..", ".."));
                    if (string.Equals(moduleId, CurrentModuleId, StringComparison.OrdinalIgnoreCase))
                    {
                        if (Directory.Exists(currentModuleRoot))
                            return currentModuleRoot;
                    }
                    else
                    {
                        string siblingModuleRoot = Path.GetFullPath(Path.Combine(currentModuleRoot, "..", moduleId));
                        if (Directory.Exists(siblingModuleRoot))
                            return siblingModuleRoot;
                    }
                }
            }
            catch (Exception ex)
            {
                ModLogger.Info(
                    "ModulePathHelper: assembly path fallback failed for " +
                    moduleId + ": " + ex.Message);
            }

            return null;
        }
    }
}
