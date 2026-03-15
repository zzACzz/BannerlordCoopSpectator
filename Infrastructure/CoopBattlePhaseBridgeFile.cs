using System;
using System.IO;

namespace CoopSpectator.Infrastructure
{
    public static class CoopBattlePhaseBridgeFile
    {
        private const string CoopSpectatorSubFolder = "CoopSpectator";
        private const string StatusFileName = "battle_phase_status.txt";
        private const string StartBattleRequestFileName = "battle_phase_start.request";

        public static string GetStatusFilePath()
        {
            return Path.Combine(GetCoopFolderPath(), StatusFileName);
        }

        public static string GetStartBattleRequestFilePath()
        {
            return Path.Combine(GetCoopFolderPath(), StartBattleRequestFileName);
        }

        public static void WriteStatus(CoopBattlePhaseStateSnapshot snapshot)
        {
            if (snapshot == null)
                return;

            try
            {
                Directory.CreateDirectory(GetCoopFolderPath());
                string[] lines =
                {
                    "Phase=" + snapshot.Phase,
                    "Source=" + (snapshot.Source ?? string.Empty),
                    "Mission=" + (snapshot.MissionName ?? string.Empty),
                    "UpdatedUtc=" + snapshot.UpdatedUtc.ToString("O")
                };
                File.WriteAllLines(GetStatusFilePath(), lines);
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopBattlePhaseBridgeFile: failed to write status file: " + ex.Message);
            }
        }

        public static CoopBattlePhaseStateSnapshot ReadStatus()
        {
            try
            {
                string path = GetStatusFilePath();
                if (!File.Exists(path))
                    return null;

                string[] lines = File.ReadAllLines(path);
                var snapshot = new CoopBattlePhaseStateSnapshot
                {
                    Phase = CoopBattlePhase.None,
                    Source = string.Empty,
                    MissionName = string.Empty,
                    UpdatedUtc = DateTime.MinValue
                };

                foreach (string rawLine in lines)
                {
                    if (string.IsNullOrWhiteSpace(rawLine))
                        continue;

                    int separatorIndex = rawLine.IndexOf('=');
                    if (separatorIndex <= 0)
                        continue;

                    string key = rawLine.Substring(0, separatorIndex).Trim();
                    string value = rawLine.Substring(separatorIndex + 1).Trim();
                    if (key.Equals("Phase", StringComparison.OrdinalIgnoreCase))
                    {
                        if (Enum.TryParse(value, ignoreCase: true, out CoopBattlePhase parsedPhase))
                            snapshot.Phase = parsedPhase;
                    }
                    else if (key.Equals("Source", StringComparison.OrdinalIgnoreCase))
                    {
                        snapshot.Source = value;
                    }
                    else if (key.Equals("Mission", StringComparison.OrdinalIgnoreCase))
                    {
                        snapshot.MissionName = value;
                    }
                    else if (key.Equals("UpdatedUtc", StringComparison.OrdinalIgnoreCase))
                    {
                        if (DateTime.TryParse(value, null, System.Globalization.DateTimeStyles.RoundtripKind, out DateTime updatedUtc))
                            snapshot.UpdatedUtc = updatedUtc;
                    }
                }

                return snapshot;
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopBattlePhaseBridgeFile: failed to read status file: " + ex.Message);
                return null;
            }
        }

        public static bool WriteStartBattleRequest(string source)
        {
            try
            {
                Directory.CreateDirectory(GetCoopFolderPath());
                string[] lines =
                {
                    "Source=" + (source ?? "unknown"),
                    "RequestedUtc=" + DateTime.UtcNow.ToString("O")
                };
                File.WriteAllLines(GetStartBattleRequestFilePath(), lines);
                return true;
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopBattlePhaseBridgeFile: failed to write start battle request: " + ex.Message);
                return false;
            }
        }

        public static bool ConsumeStartBattleRequest(out string source)
        {
            source = null;
            try
            {
                string path = GetStartBattleRequestFilePath();
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
                ModLogger.Info("CoopBattlePhaseBridgeFile: failed to consume start battle request: " + ex.Message);
                return false;
            }
        }

        private static string GetCoopFolderPath()
        {
            string docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            return Path.Combine(docs, "Mount and Blade II Bannerlord", CoopSpectatorSubFolder);
        }
    }
}
