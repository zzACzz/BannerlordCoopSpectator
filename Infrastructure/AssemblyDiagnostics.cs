using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace CoopSpectator.Infrastructure
{
    /// <summary>
    /// Runtime diagnostics for assembly load paths and versions. Call once at startup (dedicated and client)
    /// to log BaseDirectory, MainModule, and key assembly locations/versions to detect old DLL or build mismatch.
    /// </summary>
    public static class AssemblyDiagnostics
    {
        /// <summary>Маркер збірки мода для порівняння dedicated vs client логів.</summary>
        public const string BUILD_MARKER = "COOP_FIX_2026_03_08_A";

        private static readonly string[] KeyAssemblyNames = new[]
        {
            "TaleWorlds.MountAndBlade",
            "TaleWorlds.Core",
            "TaleWorlds.Library",
            "0Harmony",
            "HarmonyLib"
        };

        /// <summary>Логує AppContext.BaseDirectory, Process.MainModule.FileName, потім для кожної ключової assembly: FullName, Location, AssemblyVersion, FileVersion, MVID, LastWriteTimeUtc (якщо файл існує).</summary>
        public static void LogRuntimeLoadPaths()
        {
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory ?? "";
                string mainModule = "";
                try
                {
                    using (var p = Process.GetCurrentProcess())
                        mainModule = p.MainModule?.FileName ?? "";
                }
                catch (Exception ex) { mainModule = "(failed: " + ex.Message + ")"; }

                string prefix = (baseDir.IndexOf("Dedicated", StringComparison.OrdinalIgnoreCase) >= 0 || mainModule.IndexOf("Dedicated", StringComparison.OrdinalIgnoreCase) >= 0)
                    ? "[DedicatedDiag]" : "[CoopSpectator]";
                ModLogger.Info(prefix + " AppContext.BaseDirectory=" + baseDir);
                ModLogger.Info(prefix + " Process.MainModule.FileName=" + mainModule);
                ModLogger.Info(prefix + " BUILD_MARKER=" + BUILD_MARKER);

                bool isDedicated = baseDir.IndexOf("Dedicated", StringComparison.OrdinalIgnoreCase) >= 0
                    || mainModule.IndexOf("Dedicated", StringComparison.OrdinalIgnoreCase) >= 0;
                ModLogger.Info(prefix + " IsDedicatedProcess=" + isDedicated + " (inferred from path).");

                Assembly execAsm = Assembly.GetExecutingAssembly();
                LogOneAssembly("ExecutingAssembly", execAsm);
                LogBuildAndBinaryId(prefix, isDedicated, execAsm);

                foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    string name = asm.GetName().Name ?? "";
                    foreach (string key in KeyAssemblyNames)
                    {
                        if (name.Equals(key, StringComparison.OrdinalIgnoreCase) || name.StartsWith(key + ".", StringComparison.OrdinalIgnoreCase))
                        {
                            LogOneAssembly(name, asm);
                            break;
                        }
                    }
                }

                TryLogApplicationVersion();
            }
            catch (Exception ex)
            {
                ModLogger.Info("AssemblyDiagnostics: LogRuntimeLoadPaths failed: " + ex.ToString());
            }
        }

        private static void LogOneAssembly(string label, Assembly asm)
        {
            if (asm == null) return;
            try
            {
                string fullName = asm.FullName ?? "";
                string location = "";
                try { location = asm.Location ?? ""; } catch { location = "(reflection-only)"; }
                string asmVersion = asm.GetName().Version?.ToString() ?? "";
                string fileVersion = "";
                string productVersion = "";
                try
                {
                    var fv = asm.GetCustomAttribute<AssemblyFileVersionAttribute>();
                    if (fv != null) fileVersion = fv.Version ?? "";
                    var pv = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
                    if (pv != null) productVersion = pv.InformationalVersion ?? "";
                }
                catch { }

                string mvid = "";
                try { mvid = asm.ManifestModule.ModuleVersionId.ToString(); } catch { }

                string lastWrite = "";
                if (!string.IsNullOrEmpty(location) && File.Exists(location))
                {
                    try
                    {
                        var fi = new FileInfo(location);
                        lastWrite = fi.LastWriteTimeUtc.ToString("o");
                    }
                    catch { }
                }

                string p = "[CoopSpectator]";
                ModLogger.Info(p + " [" + label + "] FullName=" + fullName + " Location=" + location);
                ModLogger.Info(p + " [" + label + "] AssemblyVersion=" + asmVersion + " FileVersion=" + fileVersion + " ProductVersion=" + productVersion + " MVID=" + mvid + " LastWriteTimeUtc=" + lastWrite);
            }
            catch (Exception ex)
            {
                ModLogger.Info("AssemblyDiagnostics: [" + label + "] log failed: " + ex.Message);
            }
        }

        private static void TryLogApplicationVersion()
        {
            try
            {
                Type avType = Type.GetType("TaleWorlds.Library.ApplicationVersion, TaleWorlds.Library", false);
                if (avType == null) return;
                var pi = avType.GetProperty("ApplicationVersion", BindingFlags.Public | BindingFlags.Static);
                if (pi == null) return;
                object v = pi.GetValue(null);
                string prefix = (AppDomain.CurrentDomain.BaseDirectory ?? "").IndexOf("Dedicated", StringComparison.OrdinalIgnoreCase) >= 0 ? "[DedicatedDiag]" : "[CoopSpectator]";
                ModLogger.Info(prefix + " ApplicationVersion (Bannerlord build)=" + (v?.ToString() ?? "null"));
            }
            catch (Exception ex)
            {
                ModLogger.Info("[CoopSpectator] ApplicationVersion failed: " + ex.Message);
            }
        }

        /// <summary>Логує BINARY_ID (executing assembly path + version + MVID) для швидкого порівняння dedicated vs client.</summary>
        private static void LogBuildAndBinaryId(string prefix, bool isDedicated, Assembly execAsm)
        {
            try
            {
                string idKind = isDedicated ? "SERVER_BINARY_ID" : "CLIENT_BINARY_ID";
                string location = "";
                string fileVersion = "";
                string mvid = "";
                string lastWrite = "";
                try { location = execAsm?.Location ?? ""; } catch { }
                if (!string.IsNullOrEmpty(location) && File.Exists(location))
                {
                    try
                    {
                        var vi = FileVersionInfo.GetVersionInfo(location);
                        fileVersion = vi?.FileVersion ?? vi?.ProductVersion ?? "";
                        var fi = new FileInfo(location);
                        lastWrite = fi.LastWriteTimeUtc.ToString("o");
                    }
                    catch { }
                }
                try { mvid = execAsm?.ManifestModule?.ModuleVersionId.ToString() ?? ""; } catch { }
                ModLogger.Info(prefix + " " + idKind + " path=" + location + " FileVersion=" + fileVersion + " MVID=" + mvid + " LastWriteUtc=" + lastWrite);
            }
            catch (Exception ex)
            {
                ModLogger.Info(prefix + " LogBuildAndBinaryId failed: " + ex.Message);
            }
        }

        /// <summary>Перевіряє, чи завантажені assembly з очікуваного кореня (Dedicated Server vs Client). Якщо ні — логує ERROR. Не кидає виняток.</summary>
        public static void WarnIfAssemblyPathUnexpected()
        {
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory ?? "";
                bool isDedicated = baseDir.IndexOf("Dedicated", StringComparison.OrdinalIgnoreCase) >= 0;

                foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    string name = asm.GetName().Name ?? "";
                    if (!name.StartsWith("TaleWorlds.", StringComparison.OrdinalIgnoreCase)) continue;
                    string location;
                    try { location = asm.Location ?? ""; } catch { continue; }
                    if (string.IsNullOrEmpty(location)) continue;

                    bool locationIsDedicated = location.IndexOf("Dedicated", StringComparison.OrdinalIgnoreCase) >= 0;
                    bool locationIsClient = location.IndexOf("Bannerlord", StringComparison.OrdinalIgnoreCase) >= 0 && location.IndexOf("Dedicated", StringComparison.OrdinalIgnoreCase) < 0;

                    if (isDedicated && locationIsClient)
                        ModLogger.Error("AssemblyDiagnostics: Dedicated process but TaleWorlds assembly loaded from CLIENT path: " + name + " Location=" + location, null);
                    else if (!isDedicated && locationIsDedicated)
                        ModLogger.Info("AssemblyDiagnostics: Client process but TaleWorlds assembly from Dedicated path (unusual): " + name);
                }
            }
            catch (Exception ex)
            {
                ModLogger.Info("AssemblyDiagnostics: WarnIfAssemblyPathUnexpected failed: " + ex.Message);
            }
        }
    }
}
