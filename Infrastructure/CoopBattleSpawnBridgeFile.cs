using System;
using System.IO;

namespace CoopSpectator.Infrastructure
{
    internal static class CoopBattleSpawnBridgeFile
    {
        private const string CoopSpectatorSubFolder = "CoopSpectator";
        private const string SpawnNowRequestFileName = "battle_spawn_now.request";
        private const string ForceRespawnableRequestFileName = "battle_force_respawnable.request";

        public static bool WriteSpawnNowRequest(string source)
        {
            try
            {
                Directory.CreateDirectory(GetCoopFolderPath());
                string[] lines =
                {
                    "Source=" + (source ?? "unknown"),
                    "RequestedUtc=" + DateTime.UtcNow.ToString("O")
                };
                File.WriteAllLines(GetSpawnNowRequestFilePath(), lines);
                return true;
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopBattleSpawnBridgeFile: failed to write spawn request file: " + ex.Message);
                return false;
            }
        }

        public static bool ConsumeSpawnNowRequest(out string source)
        {
            source = null;

            try
            {
                string path = GetSpawnNowRequestFilePath();
                if (!File.Exists(path))
                    return false;

                string[] lines = File.ReadAllLines(path);
                foreach (string rawLine in lines)
                {
                    if (string.IsNullOrWhiteSpace(rawLine))
                        continue;

                    int separatorIndex = rawLine.IndexOf('=');
                    if (separatorIndex <= 0)
                        continue;

                    string key = rawLine.Substring(0, separatorIndex).Trim();
                    string value = rawLine.Substring(separatorIndex + 1).Trim();
                    if (key.Equals("Source", StringComparison.OrdinalIgnoreCase))
                        source = value;
                }

                File.Delete(path);
                return true;
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopBattleSpawnBridgeFile: failed to consume spawn request file: " + ex.Message);
                return false;
            }
        }

        public static bool WriteForceRespawnableRequest(string source)
        {
            try
            {
                Directory.CreateDirectory(GetCoopFolderPath());
                string[] lines =
                {
                    "Source=" + (source ?? "unknown"),
                    "RequestedUtc=" + DateTime.UtcNow.ToString("O")
                };
                File.WriteAllLines(GetForceRespawnableRequestFilePath(), lines);
                return true;
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopBattleSpawnBridgeFile: failed to write force-respawnable request file: " + ex.Message);
                return false;
            }
        }

        public static bool ConsumeForceRespawnableRequest(out string source)
        {
            source = null;

            try
            {
                string path = GetForceRespawnableRequestFilePath();
                if (!File.Exists(path))
                    return false;

                string[] lines = File.ReadAllLines(path);
                foreach (string rawLine in lines)
                {
                    if (string.IsNullOrWhiteSpace(rawLine))
                        continue;

                    int separatorIndex = rawLine.IndexOf('=');
                    if (separatorIndex <= 0)
                        continue;

                    string key = rawLine.Substring(0, separatorIndex).Trim();
                    string value = rawLine.Substring(separatorIndex + 1).Trim();
                    if (key.Equals("Source", StringComparison.OrdinalIgnoreCase))
                        source = value;
                }

                File.Delete(path);
                return true;
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopBattleSpawnBridgeFile: failed to consume force-respawnable request file: " + ex.Message);
                return false;
            }
        }

        public static void ClearPendingRequests(string source)
        {
            TryDeleteFile(GetSpawnNowRequestFilePath(), source, "spawn-now request");
            TryDeleteFile(GetForceRespawnableRequestFilePath(), source, "force-respawnable request");
        }

        private static string GetSpawnNowRequestFilePath()
        {
            return Path.Combine(GetCoopFolderPath(), SpawnNowRequestFileName);
        }

        private static string GetForceRespawnableRequestFilePath()
        {
            return Path.Combine(GetCoopFolderPath(), ForceRespawnableRequestFileName);
        }

        private static void TryDeleteFile(string path, string source, string label)
        {
            try
            {
                if (!File.Exists(path))
                    return;

                File.Delete(path);
                ModLogger.Info(
                    "CoopBattleSpawnBridgeFile: cleared " + label + ". " +
                    "Source=" + (source ?? "unknown"));
            }
            catch (Exception ex)
            {
                ModLogger.Info(
                    "CoopBattleSpawnBridgeFile: failed to clear " + label + ": " +
                    ex.Message);
            }
        }

        private static string GetCoopFolderPath()
        {
            string docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            return Path.Combine(docs, "Mount and Blade II Bannerlord", CoopSpectatorSubFolder);
        }
    }
}
