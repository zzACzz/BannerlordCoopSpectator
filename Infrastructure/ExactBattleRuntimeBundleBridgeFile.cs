using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace CoopSpectator.Infrastructure
{
    public static class ExactBattleRuntimeBundleBridgeFile
    {
        private const string CoopSpectatorSubFolder = "CoopSpectator";
        private const string BundleFileName = "battle_runtime_bundle.txt";

        public static string GetBundleFilePath()
        {
            return Path.Combine(GetCoopFolderPath(), BundleFileName);
        }

        public static void ResetBundle(string source)
        {
            try
            {
                Directory.CreateDirectory(GetCoopFolderPath());

                int processId = -1;
                string processName = string.Empty;
                try
                {
                    using (Process process = Process.GetCurrentProcess())
                    {
                        processId = process.Id;
                        processName = process.ProcessName ?? string.Empty;
                    }
                }
                catch
                {
                    processId = -1;
                    processName = string.Empty;
                }

                string baseDirectory = AppDomain.CurrentDomain.BaseDirectory ?? string.Empty;
                string executingAssemblyPath = string.Empty;
                string executingAssemblyFileVersion = string.Empty;
                string executingAssemblyMvid = string.Empty;
                try
                {
                    Assembly executingAssembly = Assembly.GetExecutingAssembly();
                    executingAssemblyPath = executingAssembly.Location ?? string.Empty;
                    executingAssemblyMvid = executingAssembly.ManifestModule.ModuleVersionId.ToString();
                    if (!string.IsNullOrWhiteSpace(executingAssemblyPath) && File.Exists(executingAssemblyPath))
                    {
                        FileVersionInfo versionInfo = FileVersionInfo.GetVersionInfo(executingAssemblyPath);
                        executingAssemblyFileVersion = versionInfo?.FileVersion ?? versionInfo?.ProductVersion ?? string.Empty;
                    }
                }
                catch
                {
                    executingAssemblyPath = string.Empty;
                    executingAssemblyFileVersion = string.Empty;
                    executingAssemblyMvid = string.Empty;
                }

                string commonDataRoot = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
                string bannerlordRoot = Path.Combine(commonDataRoot, "Mount and Blade II Bannerlord");
                string logRoot = Path.Combine(bannerlordRoot, "logs");
                string crashRoot = Path.Combine(bannerlordRoot, "crashes");
                string expectedRglLogPath =
                    processId >= 0
                        ? Path.Combine(logRoot, "rgl_log_" + processId + ".txt")
                        : string.Empty;
                string expectedWatchdogLogPath =
                    processId >= 0
                        ? Path.Combine(logRoot, "watchdog_log_" + processId + ".txt")
                        : string.Empty;

                var lines = new List<string>
                {
                    "SessionStartedUtc=" + DateTime.UtcNow.ToString("O"),
                    "Source=" + (source ?? string.Empty),
                    "BuildMarker=" + AssemblyDiagnostics.BUILD_MARKER,
                    "ProcessId=" + processId,
                    "ProcessName=" + processName,
                    "BaseDirectory=" + baseDirectory,
                    "ExecutingAssemblyPath=" + executingAssemblyPath,
                    "ExecutingAssemblyFileVersion=" + executingAssemblyFileVersion,
                    "ExecutingAssemblyMvid=" + executingAssemblyMvid,
                    "BannerlordBuildVersion=" + TryResolveBannerlordBuildVersion(),
                    "ExpectedRglLogPath=" + expectedRglLogPath,
                    "ExpectedWatchdogLogPath=" + expectedWatchdogLogPath,
                    "CrashRootDirectory=" + crashRoot,
                    "TraceFilePath=" + ExactBattleAgentSpawnTraceBridgeFile.GetTraceFilePath(),
                    "CompatibilityReportPath=" + ExactBattleEntryCompatibilityBridgeFile.GetReportFilePath()
                };

                File.WriteAllLines(GetBundleFilePath(), lines.ToArray());
            }
            catch (Exception ex)
            {
                ModLogger.Info("ExactBattleRuntimeBundleBridgeFile: failed to reset bundle: " + ex.Message);
            }
        }

        public static void AppendMissionContext(
            string battleId,
            string missionName,
            string source,
            string additionalDetails = null)
        {
            AppendRecord(
                "Utc=" + DateTime.UtcNow.ToString("O") +
                " Event=mission-context" +
                " BattleId=" + (battleId ?? string.Empty) +
                " MissionName=" + (missionName ?? string.Empty) +
                " Source=" + (source ?? string.Empty) +
                (string.IsNullOrWhiteSpace(additionalDetails) ? string.Empty : " " + additionalDetails));
        }

        public static void AppendContractEvent(string eventName, string details)
        {
            AppendRecord(
                "Utc=" + DateTime.UtcNow.ToString("O") +
                " Event=" + (eventName ?? string.Empty) +
                (string.IsNullOrWhiteSpace(details) ? string.Empty : " " + details));
        }

        private static void AppendRecord(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return;

            try
            {
                Directory.CreateDirectory(GetCoopFolderPath());
                if (!File.Exists(GetBundleFilePath()))
                    ResetBundle("auto-create");

                using (FileStream stream = new FileStream(GetBundleFilePath(), FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
                using (StreamWriter writer = new StreamWriter(stream))
                {
                    writer.WriteLine(line);
                }
            }
            catch (Exception ex)
            {
                ModLogger.Info("ExactBattleRuntimeBundleBridgeFile: failed to append bundle record: " + ex.Message);
            }
        }

        private static string TryResolveBannerlordBuildVersion()
        {
            try
            {
                Type applicationVersionType = Type.GetType("TaleWorlds.Library.ApplicationVersion, TaleWorlds.Library", false);
                PropertyInfo applicationVersionProperty = applicationVersionType?.GetProperty("ApplicationVersion", BindingFlags.Public | BindingFlags.Static);
                object applicationVersion = applicationVersionProperty?.GetValue(null);
                return applicationVersion?.ToString() ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string GetCoopFolderPath()
        {
            string docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            return Path.Combine(docs, "Mount and Blade II Bannerlord", CoopSpectatorSubFolder);
        }
    }
}
