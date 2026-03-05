using System;
using System.IO;
using System.Reflection;

namespace CoopSpectator.Infrastructure
{
    /// <summary>
    /// Визначення шляху до папки модуля для завантаження ModuleData (multiplayer_strings.xml тощо).
    /// Варіант 1: TaleWorlds.ModuleManager.ModuleHelper.GetModuleFullPath(moduleId).
    /// Варіант 2 (fallback): шлях від виконуваної збірки (Modules\ModuleId\bin\... → два рівні вгору = корінь модуля).
    /// </summary>
    public static class ModulePathHelper
    {
        private const string ModuleId = "CoopSpectator";

        /// <summary>Повертає повний шлях до файлу у ModuleData модуля, або null якщо не вдалося визначити.</summary>
        public static string GetModuleDataFilePath(string relativePathInModuleData)
        {
            string moduleRoot = TryGetModuleRootPath();
            if (string.IsNullOrEmpty(moduleRoot))
                return null;
            return Path.Combine(moduleRoot, "ModuleData", relativePathInModuleData);
        }

        /// <summary>Корінь модуля (папка CoopSpectator з SubModule.xml).</summary>
        private static string TryGetModuleRootPath()
        {
            // Варіант 1: ModuleHelper.GetModuleFullPath (рефлексія, щоб не додавати референс на TaleWorlds.ModuleManager).
            try
            {
                Type moduleHelperType = Type.GetType("TaleWorlds.ModuleManager.ModuleHelper, TaleWorlds.MountAndBlade", throwOnError: false)
                    ?? Type.GetType("TaleWorlds.ModuleManager.ModuleHelper, TaleWorlds.ModuleManager", throwOnError: false);
                if (moduleHelperType != null)
                {
                    MethodInfo getPath = moduleHelperType.GetMethod("GetModuleFullPath", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(string) }, null);
                    if (getPath != null)
                    {
                        string path = getPath.Invoke(null, new object[] { ModuleId }) as string;
                        if (!string.IsNullOrEmpty(path))
                            return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    }
                }
            }
            catch (Exception ex)
            {
                ModLogger.Info("ModulePathHelper: ModuleHelper.GetModuleFullPath failed: " + ex.Message);
            }

            // Варіант 2: від виконуваної збірки (DLL у Modules\CoopSpectator\bin\Win64_Shipping_Client → ..\.. = корінь модуля).
            try
            {
                string binDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                if (!string.IsNullOrEmpty(binDir))
                {
                    string moduleRoot = Path.GetFullPath(Path.Combine(binDir, "..", ".."));
                    if (Directory.Exists(moduleRoot))
                        return moduleRoot;
                }
            }
            catch (Exception ex)
            {
                ModLogger.Info("ModulePathHelper: Assembly path fallback failed: " + ex.Message);
            }

            return null;
        }
    }
}
