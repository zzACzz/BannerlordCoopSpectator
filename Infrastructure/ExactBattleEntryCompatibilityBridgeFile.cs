using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CoopSpectator.Infrastructure
{
    public static class ExactBattleEntryCompatibilityBridgeFile
    {
        private const string CoopSpectatorSubFolder = "CoopSpectator";
        private const string ReportFileName = "battle_entry_compatibility.txt";

        public static string GetReportFilePath()
        {
            return Path.Combine(GetCoopFolderPath(), ReportFileName);
        }

        public static void WriteReport(
            string battleId,
            DateTime snapshotUpdatedUtc,
            string summaryLine,
            IEnumerable<string> entryLines)
        {
            try
            {
                Directory.CreateDirectory(GetCoopFolderPath());

                var lines = new List<string>
                {
                    "BattleId=" + (battleId ?? string.Empty),
                    "SnapshotUpdatedUtc=" + snapshotUpdatedUtc.ToString("O"),
                    "GeneratedUtc=" + DateTime.UtcNow.ToString("O"),
                    "Summary=" + (summaryLine ?? string.Empty)
                };

                if (entryLines != null)
                    lines.AddRange(entryLines.Where(line => !string.IsNullOrWhiteSpace(line)));

                File.WriteAllLines(GetReportFilePath(), lines.ToArray());
            }
            catch (Exception ex)
            {
                ModLogger.Info("ExactBattleEntryCompatibilityBridgeFile: failed to write report: " + ex.Message);
            }
        }

        private static string GetCoopFolderPath()
        {
            string docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            return Path.Combine(docs, "Mount and Blade II Bannerlord", CoopSpectatorSubFolder);
        }
    }
}
