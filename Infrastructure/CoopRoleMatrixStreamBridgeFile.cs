using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CoopSpectator.Infrastructure
{
    public static class CoopRoleMatrixStreamBridgeFile
    {
        private const string CoopSpectatorSubFolder = "CoopSpectator";
        private const string ProgressFileName = "role_matrix_stream_progress.txt";
        private const string UnsafeFileName = "role_matrix_unsafe.csv";

        public sealed class ProgressSnapshot
        {
            public string BattleId { get; set; }
            public string MissionName { get; set; }
            public string Source { get; set; }
            public string State { get; set; }
            public int TotalMatrices { get; set; }
            public int SkippedUnsafeMatrices { get; set; }
            public int Cursor { get; set; }
            public int Wave { get; set; }
            public int ActiveCount { get; set; }
            public string ActiveMatrixIds { get; set; }
            public string ActiveEntryIds { get; set; }
            public string ActiveSlotSummary { get; set; }
            public DateTime UpdatedUtc { get; set; }
        }

        public sealed class UnsafeMatrixEntry
        {
            public string MatrixId { get; set; }
            public string Reason { get; set; }
            public string Source { get; set; }
            public string SlotSummary { get; set; }
            public DateTime UpdatedUtc { get; set; }
        }

        public static string GetProgressFilePath()
        {
            return Path.Combine(GetCoopFolderPath(), ProgressFileName);
        }

        public static string GetUnsafeFilePath()
        {
            return Path.Combine(GetCoopFolderPath(), UnsafeFileName);
        }

        public static HashSet<string> ReadUnsafeMatrixIds()
        {
            return new HashSet<string>(
                ReadUnsafeEntries()
                    .Select(entry => entry.MatrixId)
                    .Where(matrixId => !string.IsNullOrWhiteSpace(matrixId)),
                StringComparer.OrdinalIgnoreCase);
        }

        public static List<UnsafeMatrixEntry> ReadUnsafeEntries()
        {
            var entries = new List<UnsafeMatrixEntry>();
            try
            {
                foreach (string line in AtomicBridgeFileIO.ReadAllLinesShared(GetUnsafeFilePath()))
                {
                    if (string.IsNullOrWhiteSpace(line) ||
                        line.StartsWith("MatrixId,", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    string[] fields = ParseCsvLine(line);
                    if (fields.Length == 0 || string.IsNullOrWhiteSpace(fields[0]))
                        continue;

                    entries.Add(new UnsafeMatrixEntry
                    {
                        MatrixId = fields.Length > 0 ? fields[0] : string.Empty,
                        Reason = fields.Length > 1 ? fields[1] : string.Empty,
                        UpdatedUtc = fields.Length > 2 && DateTime.TryParse(fields[2], out DateTime updatedUtc)
                            ? updatedUtc
                            : DateTime.MinValue,
                        Source = fields.Length > 3 ? fields[3] : string.Empty,
                        SlotSummary = fields.Length > 4 ? fields[4] : string.Empty
                    });
                }
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopRoleMatrixStreamBridgeFile: failed to read unsafe matrix table: " + ex.Message);
            }

            return entries
                .GroupBy(entry => entry.MatrixId ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.Last())
                .OrderBy(entry => entry.MatrixId, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static bool AddUnsafeMatrix(string matrixId, string reason, string source, string slotSummary)
        {
            return AddUnsafeMatrices(new[] { matrixId }, reason, source, slotSummary);
        }

        public static bool AddUnsafeMatrices(IEnumerable<string> matrixIds, string reason, string source, string slotSummary)
        {
            try
            {
                var entriesById = ReadUnsafeEntries()
                    .Where(entry => !string.IsNullOrWhiteSpace(entry.MatrixId))
                    .ToDictionary(entry => entry.MatrixId, StringComparer.OrdinalIgnoreCase);

                DateTime nowUtc = DateTime.UtcNow;
                foreach (string rawMatrixId in matrixIds ?? Enumerable.Empty<string>())
                {
                    string matrixId = NormalizeMatrixId(rawMatrixId);
                    if (string.IsNullOrWhiteSpace(matrixId))
                        continue;

                    entriesById[matrixId] = new UnsafeMatrixEntry
                    {
                        MatrixId = matrixId,
                        Reason = string.IsNullOrWhiteSpace(reason) ? "manual" : reason.Trim(),
                        Source = string.IsNullOrWhiteSpace(source) ? "unknown" : source.Trim(),
                        SlotSummary = slotSummary ?? string.Empty,
                        UpdatedUtc = nowUtc
                    };
                }

                WriteUnsafeEntries(entriesById.Values);
                return true;
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopRoleMatrixStreamBridgeFile: failed to add unsafe matrix entry: " + ex.Message);
                return false;
            }
        }

        public static bool RemoveUnsafeMatrix(string matrixId)
        {
            try
            {
                string normalized = NormalizeMatrixId(matrixId);
                List<UnsafeMatrixEntry> entries = ReadUnsafeEntries()
                    .Where(entry => !string.Equals(entry.MatrixId, normalized, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                WriteUnsafeEntries(entries);
                return true;
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopRoleMatrixStreamBridgeFile: failed to remove unsafe matrix entry: " + ex.Message);
                return false;
            }
        }

        public static bool ClearUnsafeMatrices()
        {
            try
            {
                WriteUnsafeEntries(Array.Empty<UnsafeMatrixEntry>());
                return true;
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopRoleMatrixStreamBridgeFile: failed to clear unsafe matrix table: " + ex.Message);
                return false;
            }
        }

        public static string FormatUnsafeEntries(int maxEntries)
        {
            List<UnsafeMatrixEntry> entries = ReadUnsafeEntries();
            if (entries.Count == 0)
                return "unsafe=0 path=" + GetUnsafeFilePath();

            int safeMax = Math.Max(1, maxEntries);
            string sample = string.Join("; ", entries
                .Take(safeMax)
                .Select(entry => entry.MatrixId + ":" + (entry.Reason ?? string.Empty)));
            return "unsafe=" + entries.Count +
                   " sample=[" + sample + "]" +
                   " path=" + GetUnsafeFilePath();
        }

        public static bool WriteProgress(ProgressSnapshot snapshot)
        {
            if (snapshot == null)
                return false;

            try
            {
                snapshot.UpdatedUtc = snapshot.UpdatedUtc == DateTime.MinValue
                    ? DateTime.UtcNow
                    : snapshot.UpdatedUtc;

                AtomicBridgeFileIO.WriteAllLines(
                    GetProgressFilePath(),
                    new[]
                    {
                        "BattleId=" + (snapshot.BattleId ?? string.Empty),
                        "MissionName=" + (snapshot.MissionName ?? string.Empty),
                        "Source=" + (snapshot.Source ?? string.Empty),
                        "State=" + (snapshot.State ?? string.Empty),
                        "TotalMatrices=" + snapshot.TotalMatrices,
                        "SkippedUnsafeMatrices=" + snapshot.SkippedUnsafeMatrices,
                        "Cursor=" + snapshot.Cursor,
                        "Wave=" + snapshot.Wave,
                        "ActiveCount=" + snapshot.ActiveCount,
                        "ActiveMatrixIds=" + (snapshot.ActiveMatrixIds ?? string.Empty),
                        "ActiveEntryIds=" + (snapshot.ActiveEntryIds ?? string.Empty),
                        "ActiveSlotSummary=" + (snapshot.ActiveSlotSummary ?? string.Empty),
                        "UpdatedUtc=" + snapshot.UpdatedUtc.ToString("O")
                    });
                return true;
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopRoleMatrixStreamBridgeFile: failed to write stream progress: " + ex.Message);
                return false;
            }
        }

        public static ProgressSnapshot ReadProgress()
        {
            var snapshot = new ProgressSnapshot();
            try
            {
                foreach (string line in AtomicBridgeFileIO.ReadAllLinesShared(GetProgressFilePath()))
                {
                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    int separator = line.IndexOf('=');
                    if (separator <= 0)
                        continue;

                    string key = line.Substring(0, separator).Trim();
                    string value = line.Substring(separator + 1).Trim();
                    ApplyProgressField(snapshot, key, value);
                }
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopRoleMatrixStreamBridgeFile: failed to read stream progress: " + ex.Message);
            }

            return snapshot;
        }

        public static bool ClearProgress()
        {
            try
            {
                AtomicBridgeFileIO.WriteAllLines(GetProgressFilePath(), Array.Empty<string>());
                return true;
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopRoleMatrixStreamBridgeFile: failed to clear stream progress: " + ex.Message);
                return false;
            }
        }

        public static bool AddLastProgressActiveMatricesToUnsafe(string reason, string source)
        {
            ProgressSnapshot progress = ReadProgress();
            string[] matrixIds = SplitMatrixIds(progress.ActiveMatrixIds);
            if (matrixIds.Length == 0)
                return false;

            return AddUnsafeMatrices(
                matrixIds,
                string.IsNullOrWhiteSpace(reason) ? "suspect-last-wave" : reason,
                string.IsNullOrWhiteSpace(source) ? "matrix_unsafe add_last" : source,
                progress.ActiveSlotSummary);
        }

        public static string FormatProgress()
        {
            ProgressSnapshot progress = ReadProgress();
            return "state=" + (progress.State ?? string.Empty) +
                   " cursor=" + progress.Cursor + "/" + progress.TotalMatrices +
                   " wave=" + progress.Wave +
                   " active=" + progress.ActiveCount +
                   " activeMatrices=[" + (progress.ActiveMatrixIds ?? string.Empty) + "]" +
                   " path=" + GetProgressFilePath();
        }

        private static void WriteUnsafeEntries(IEnumerable<UnsafeMatrixEntry> entries)
        {
            var lines = new List<string>
            {
                "MatrixId,Reason,UpdatedUtc,Source,SlotSummary"
            };

            foreach (UnsafeMatrixEntry entry in (entries ?? Enumerable.Empty<UnsafeMatrixEntry>())
                .Where(entry => !string.IsNullOrWhiteSpace(entry?.MatrixId))
                .OrderBy(entry => entry.MatrixId, StringComparer.OrdinalIgnoreCase))
            {
                lines.Add(string.Join(
                    ",",
                    Csv(entry.MatrixId),
                    Csv(entry.Reason),
                    Csv(entry.UpdatedUtc == DateTime.MinValue ? string.Empty : entry.UpdatedUtc.ToString("O")),
                    Csv(entry.Source),
                    Csv(entry.SlotSummary)));
            }

            AtomicBridgeFileIO.WriteAllLines(GetUnsafeFilePath(), lines);
        }

        private static void ApplyProgressField(ProgressSnapshot snapshot, string key, string value)
        {
            if (snapshot == null || string.IsNullOrWhiteSpace(key))
                return;

            switch (key)
            {
                case "BattleId":
                    snapshot.BattleId = value;
                    return;
                case "MissionName":
                    snapshot.MissionName = value;
                    return;
                case "Source":
                    snapshot.Source = value;
                    return;
                case "State":
                    snapshot.State = value;
                    return;
                case "TotalMatrices":
                    snapshot.TotalMatrices = ParseInt(value);
                    return;
                case "SkippedUnsafeMatrices":
                    snapshot.SkippedUnsafeMatrices = ParseInt(value);
                    return;
                case "Cursor":
                    snapshot.Cursor = ParseInt(value);
                    return;
                case "Wave":
                    snapshot.Wave = ParseInt(value);
                    return;
                case "ActiveCount":
                    snapshot.ActiveCount = ParseInt(value);
                    return;
                case "ActiveMatrixIds":
                    snapshot.ActiveMatrixIds = value;
                    return;
                case "ActiveEntryIds":
                    snapshot.ActiveEntryIds = value;
                    return;
                case "ActiveSlotSummary":
                    snapshot.ActiveSlotSummary = value;
                    return;
                case "UpdatedUtc":
                    snapshot.UpdatedUtc = DateTime.TryParse(value, out DateTime updatedUtc)
                        ? updatedUtc
                        : DateTime.MinValue;
                    return;
            }
        }

        private static int ParseInt(string value)
        {
            return int.TryParse(value, out int parsed) ? parsed : 0;
        }

        private static string NormalizeMatrixId(string matrixId)
        {
            return string.IsNullOrWhiteSpace(matrixId)
                ? string.Empty
                : matrixId.Trim().ToUpperInvariant();
        }

        private static string[] SplitMatrixIds(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return Array.Empty<string>();

            return raw
                .Split(new[] { ',', ';', '|', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(NormalizeMatrixId)
                .Where(matrixId => !string.IsNullOrWhiteSpace(matrixId))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static string Csv(string value)
        {
            string safe = value ?? string.Empty;
            if (safe.IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0)
                return safe;

            return "\"" + safe.Replace("\"", "\"\"") + "\"";
        }

        private static string[] ParseCsvLine(string line)
        {
            if (line == null)
                return Array.Empty<string>();

            var fields = new List<string>();
            var current = new List<char>();
            bool quoted = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (quoted)
                {
                    if (c == '"' && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Add('"');
                        i++;
                    }
                    else if (c == '"')
                    {
                        quoted = false;
                    }
                    else
                    {
                        current.Add(c);
                    }
                }
                else if (c == ',')
                {
                    fields.Add(new string(current.ToArray()));
                    current.Clear();
                }
                else if (c == '"')
                {
                    quoted = true;
                }
                else
                {
                    current.Add(c);
                }
            }

            fields.Add(new string(current.ToArray()));
            return fields.ToArray();
        }

        private static string GetCoopFolderPath()
        {
            string docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            return Path.Combine(docs, "Mount and Blade II Bannerlord", CoopSpectatorSubFolder);
        }
    }
}
