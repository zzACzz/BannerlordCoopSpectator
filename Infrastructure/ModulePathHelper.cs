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
        private const string ExplicitGameRootEnvVar = "BANNERLORD_GAME_ROOT";
        private const string BannerlordGameFolderName = "Mount & Blade II Bannerlord";
        private const string DedicatedServerFolderName = "Mount & Blade II Dedicated Server";

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
                string explicitGameRoot = TryNormalizeRootDirectory(Environment.GetEnvironmentVariable(ExplicitGameRootEnvVar));
                if (!string.IsNullOrEmpty(explicitGameRoot))
                {
                    string explicitModuleRoot = Path.Combine(explicitGameRoot, "Modules", moduleId);
                    if (Directory.Exists(explicitModuleRoot))
                    {
                        ModLogger.Info(
                            "ModulePathHelper: resolved module root via explicit game root environment. " +
                            "moduleId=" + moduleId +
                            " root=" + explicitModuleRoot + ".");
                        return explicitModuleRoot;
                    }
                }
            }
            catch (Exception ex)
            {
                ModLogger.Info(
                    "ModulePathHelper: explicit game root environment fallback failed for " +
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

                        string modulesRoot = Path.GetDirectoryName(currentModuleRoot);
                        string installationRoot = !string.IsNullOrEmpty(modulesRoot) ? Path.GetDirectoryName(modulesRoot) : null;
                        string currentInstallName = !string.IsNullOrEmpty(installationRoot) ? Path.GetFileName(installationRoot) : null;
                        if (string.Equals(currentInstallName, DedicatedServerFolderName, StringComparison.OrdinalIgnoreCase))
                        {
                            string commonRoot = Path.GetDirectoryName(installationRoot);
                            if (!string.IsNullOrEmpty(commonRoot))
                            {
                                string siblingGameModuleRoot = Path.Combine(commonRoot, BannerlordGameFolderName, "Modules", moduleId);
                                if (Directory.Exists(siblingGameModuleRoot))
                                {
                                    ModLogger.Info(
                                        "ModulePathHelper: resolved module root via sibling Bannerlord game install. " +
                                        "moduleId=" + moduleId +
                                        " root=" + siblingGameModuleRoot + ".");
                                    return siblingGameModuleRoot;
                                }
                            }
                        }
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

        private static string TryNormalizeRootDirectory(string rawPath)
        {
            if (string.IsNullOrWhiteSpace(rawPath))
                return null;

            try
            {
                string normalizedPath = rawPath.Trim().Trim('"');
                if (File.Exists(normalizedPath))
                    normalizedPath = Path.GetDirectoryName(normalizedPath);

                if (string.IsNullOrWhiteSpace(normalizedPath))
                    return null;

                if (string.Equals(Path.GetFileName(normalizedPath), "Modules", StringComparison.OrdinalIgnoreCase))
                    normalizedPath = Path.GetDirectoryName(normalizedPath);

                string fullPath = Path.GetFullPath(normalizedPath);
                return Directory.Exists(fullPath)
                    ? fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    : null;
            }
            catch
            {
                return null;
            }
        }
    }
}
