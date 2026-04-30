using System;
using System.Collections.Generic;
using System.IO;

namespace CoopSpectator.Infrastructure
{
    public static class ExactBattleAgentSpawnTraceBridgeFile
    {
        private const string CoopSpectatorSubFolder = "CoopSpectator";
        private const string TraceFileName = "battle_agent_spawn_trace.txt";

        public static string GetTraceFilePath()
        {
            return Path.Combine(GetCoopFolderPath(), TraceFileName);
        }

        public static void ResetTrace(
            string battleId,
            string missionName,
            DateTime snapshotUpdatedUtc,
            string source)
        {
            try
            {
                Directory.CreateDirectory(GetCoopFolderPath());

                var lines = new List<string>
                {
                    "BattleId=" + (battleId ?? string.Empty),
                    "MissionName=" + (missionName ?? string.Empty),
                    "SnapshotUpdatedUtc=" + (snapshotUpdatedUtc == DateTime.MinValue ? string.Empty : snapshotUpdatedUtc.ToString("O")),
                    "SessionStartedUtc=" + DateTime.UtcNow.ToString("O"),
                    "Source=" + (source ?? string.Empty)
                };

                using (FileStream stream = new FileStream(GetTraceFilePath(), FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
                using (StreamWriter writer = new StreamWriter(stream))
                {
                    foreach (string line in lines)
                        writer.WriteLine(line);
                }
            }
            catch (Exception ex)
            {
                ModLogger.Info("ExactBattleAgentSpawnTraceBridgeFile: failed to reset trace: " + ex.Message);
            }
        }

        public static void AppendRecord(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return;

            try
            {
                Directory.CreateDirectory(GetCoopFolderPath());

                using (FileStream stream = new FileStream(GetTraceFilePath(), FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
                using (StreamWriter writer = new StreamWriter(stream))
                {
                    writer.WriteLine(line);
                }
            }
            catch (Exception ex)
            {
                ModLogger.Info("ExactBattleAgentSpawnTraceBridgeFile: failed to append trace record: " + ex.Message);
            }
        }

        private static string GetCoopFolderPath()
        {
            string docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            return Path.Combine(docs, "Mount and Blade II Bannerlord", CoopSpectatorSubFolder);
        }
    }
}
