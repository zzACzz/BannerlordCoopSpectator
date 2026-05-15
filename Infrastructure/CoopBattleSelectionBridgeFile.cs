using System;
using System.IO;

namespace CoopSpectator.Infrastructure
{
    public static class CoopBattleSelectionBridgeFile
    {
        private const string CoopSpectatorSubFolder = "CoopSpectator";
        private const string SelectSideRequestFileName = "battle_select_side.request";
        private const string SelectTroopRequestFileName = "battle_select_troop.request";
        private const string SpectatorRequestFileName = "battle_select_spectator.request";
        private const string CurrentSelectionFileName = "battle_selection_current.txt";
        private static readonly object CurrentSelectionCacheLock = new object();
        private static SelectionBridgeSnapshot _lastValidCurrentSelection;

        public sealed class SelectionBridgeSnapshot
        {
            public string Side { get; set; }
            public string TroopOrEntryId { get; set; }
            public string Source { get; set; }
            public DateTime UpdatedUtc { get; set; }
        }

        public static bool WriteSelectSideRequest(string side, string source)
        {
            bool written = WriteRequest(
                GetSelectSideRequestFilePath(),
                side,
                null,
                source);
            if (written)
                UpdateCurrentSelection(side, null, source);
            return written;
        }

        public static bool WriteSelectTroopRequest(string troopOrEntryId, string source)
        {
            bool written = WriteRequest(
                GetSelectTroopRequestFilePath(),
                null,
                troopOrEntryId,
                source);
            if (written)
                UpdateCurrentSelection(null, troopOrEntryId, source);
            return written;
        }

        public static bool WriteSpectatorRequest(string source)
        {
            try
            {
                ClearAll(source + " spectator");
                Directory.CreateDirectory(GetCoopFolderPath());
                string[] lines =
                {
                    "Mode=Spectator",
                    "Source=" + (source ?? "unknown"),
                    "RequestedUtc=" + DateTime.UtcNow.ToString("O")
                };
                AtomicBridgeFileIO.WriteAllLines(GetSpectatorRequestFilePath(), lines);
                return true;
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopBattleSelectionBridgeFile: failed to write spectator request file: " + ex.Message);
                return false;
            }
        }

        public static bool ConsumeSelectSideRequest(out string side, out string source)
        {
            side = null;
            source = null;
            return ConsumeRequest(GetSelectSideRequestFilePath(), out side, out _, out source);
        }

        public static bool ConsumeSelectTroopRequest(out string troopOrEntryId, out string source)
        {
            troopOrEntryId = null;
            source = null;
            return ConsumeRequest(GetSelectTroopRequestFilePath(), out _, out troopOrEntryId, out source);
        }

        public static bool ConsumeSpectatorRequest(out string source)
        {
            source = null;

            try
            {
                string path = GetSpectatorRequestFilePath();
                if (!File.Exists(path))
                    return false;

                string[] lines = AtomicBridgeFileIO.ReadAllLinesShared(path);
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
                ModLogger.Info("CoopBattleSelectionBridgeFile: failed to consume spectator request file: " + ex.Message);
                return false;
            }
        }

        public static SelectionBridgeSnapshot ReadCurrentSelection()
        {
            try
            {
                string path = GetCurrentSelectionFilePath();
                if (!File.Exists(path))
                    return null;

                string[] lines = AtomicBridgeFileIO.ReadAllLinesShared(path);
                SelectionBridgeSnapshot snapshot = new SelectionBridgeSnapshot
                {
                    Side = null,
                    TroopOrEntryId = null,
                    Source = string.Empty,
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
                    if (key.Equals("Side", StringComparison.OrdinalIgnoreCase))
                        snapshot.Side = value;
                    else if (key.Equals("TroopOrEntryId", StringComparison.OrdinalIgnoreCase))
                        snapshot.TroopOrEntryId = value;
                    else if (key.Equals("Source", StringComparison.OrdinalIgnoreCase))
                        snapshot.Source = value;
                    else if (key.Equals("UpdatedUtc", StringComparison.OrdinalIgnoreCase) &&
                             DateTime.TryParse(value, null, System.Globalization.DateTimeStyles.RoundtripKind, out DateTime updatedUtc))
                        snapshot.UpdatedUtc = updatedUtc;
                }

                if (!IsValidCurrentSelection(snapshot))
                    return GetLastValidCurrentSelection();

                RememberCurrentSelection(snapshot);
                return snapshot;
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopBattleSelectionBridgeFile: failed to read current selection file: " + ex.Message);
                return GetLastValidCurrentSelection();
            }
        }

        public static void ClearAll(string source)
        {
            TryDeleteFile(GetSelectSideRequestFilePath(), source, "select-side request");
            TryDeleteFile(GetSelectTroopRequestFilePath(), source, "select-troop request");
            TryDeleteFile(GetSpectatorRequestFilePath(), source, "spectator request");
            TryDeleteFile(GetCurrentSelectionFilePath(), source, "current selection");
        }

        private static bool WriteRequest(string path, string side, string troopOrEntryId, string source)
        {
            try
            {
                Directory.CreateDirectory(GetCoopFolderPath());
                string[] lines =
                {
                    "Side=" + (side ?? string.Empty),
                    "TroopOrEntryId=" + (troopOrEntryId ?? string.Empty),
                    "Source=" + (source ?? "unknown"),
                    "RequestedUtc=" + DateTime.UtcNow.ToString("O")
                };
                AtomicBridgeFileIO.WriteAllLines(path, lines);
                return true;
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopBattleSelectionBridgeFile: failed to write request file: " + ex.Message);
                return false;
            }
        }

        private static bool ConsumeRequest(string path, out string side, out string troopOrEntryId, out string source)
        {
            side = null;
            troopOrEntryId = null;
            source = null;

            try
            {
                if (!File.Exists(path))
                    return false;

                string[] lines = AtomicBridgeFileIO.ReadAllLinesShared(path);
                foreach (string rawLine in lines)
                {
                    if (string.IsNullOrWhiteSpace(rawLine))
                        continue;

                    int separatorIndex = rawLine.IndexOf('=');
                    if (separatorIndex <= 0)
                        continue;

                    string key = rawLine.Substring(0, separatorIndex).Trim();
                    string value = rawLine.Substring(separatorIndex + 1).Trim();
                    if (key.Equals("Side", StringComparison.OrdinalIgnoreCase))
                        side = value;
                    else if (key.Equals("TroopOrEntryId", StringComparison.OrdinalIgnoreCase))
                        troopOrEntryId = value;
                    else if (key.Equals("Source", StringComparison.OrdinalIgnoreCase))
                        source = value;
                }

                File.Delete(path);
                return true;
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopBattleSelectionBridgeFile: failed to consume request file: " + ex.Message);
                return false;
            }
        }

        private static string GetSelectSideRequestFilePath()
        {
            return Path.Combine(GetCoopFolderPath(), SelectSideRequestFileName);
        }

        private static string GetSelectTroopRequestFilePath()
        {
            return Path.Combine(GetCoopFolderPath(), SelectTroopRequestFileName);
        }

        private static string GetCurrentSelectionFilePath()
        {
            return Path.Combine(GetCoopFolderPath(), CurrentSelectionFileName);
        }

        private static string GetSpectatorRequestFilePath()
        {
            return Path.Combine(GetCoopFolderPath(), SpectatorRequestFileName);
        }

        private static void TryDeleteFile(string path, string source, string label)
        {
            try
            {
                if (!File.Exists(path))
                    return;

                File.Delete(path);
                ModLogger.Info(
                    "CoopBattleSelectionBridgeFile: cleared " + label + ". " +
                    "Source=" + (source ?? "unknown"));
            }
            catch (Exception ex)
            {
                ModLogger.Info(
                    "CoopBattleSelectionBridgeFile: failed to clear " + label + ": " +
                    ex.Message);
            }
        }

        private static void UpdateCurrentSelection(string side, string troopOrEntryId, string source)
        {
            try
            {
                Directory.CreateDirectory(GetCoopFolderPath());
                SelectionBridgeSnapshot snapshot = ReadCurrentSelection() ?? new SelectionBridgeSnapshot();
                if (!string.IsNullOrWhiteSpace(side))
                    snapshot.Side = side.Trim();
                if (!string.IsNullOrWhiteSpace(troopOrEntryId))
                    snapshot.TroopOrEntryId = troopOrEntryId.Trim();
                snapshot.Source = source ?? "unknown";
                snapshot.UpdatedUtc = DateTime.UtcNow;

                string[] lines =
                {
                    "Side=" + (snapshot.Side ?? string.Empty),
                    "TroopOrEntryId=" + (snapshot.TroopOrEntryId ?? string.Empty),
                    "Source=" + (snapshot.Source ?? string.Empty),
                    "UpdatedUtc=" + snapshot.UpdatedUtc.ToString("O")
                };
                AtomicBridgeFileIO.WriteAllLines(GetCurrentSelectionFilePath(), lines);
                RememberCurrentSelection(snapshot);
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopBattleSelectionBridgeFile: failed to update current selection file: " + ex.Message);
            }
        }

        private static bool IsValidCurrentSelection(SelectionBridgeSnapshot snapshot)
        {
            return snapshot != null &&
                   snapshot.UpdatedUtc != DateTime.MinValue &&
                   (!string.IsNullOrWhiteSpace(snapshot.Side) ||
                    !string.IsNullOrWhiteSpace(snapshot.TroopOrEntryId) ||
                    !string.IsNullOrWhiteSpace(snapshot.Source));
        }

        private static void RememberCurrentSelection(SelectionBridgeSnapshot snapshot)
        {
            if (snapshot == null)
                return;

            lock (CurrentSelectionCacheLock)
            {
                _lastValidCurrentSelection = CloneCurrentSelection(snapshot);
            }
        }

        private static SelectionBridgeSnapshot GetLastValidCurrentSelection()
        {
            lock (CurrentSelectionCacheLock)
            {
                return CloneCurrentSelection(_lastValidCurrentSelection);
            }
        }

        private static SelectionBridgeSnapshot CloneCurrentSelection(SelectionBridgeSnapshot snapshot)
        {
            if (snapshot == null)
                return null;

            return new SelectionBridgeSnapshot
            {
                Side = snapshot.Side,
                TroopOrEntryId = snapshot.TroopOrEntryId,
                Source = snapshot.Source,
                UpdatedUtc = snapshot.UpdatedUtc
            };
        }

        private static string GetCoopFolderPath()
        {
            string docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            return Path.Combine(docs, "Mount and Blade II Bannerlord", CoopSpectatorSubFolder);
        }
    }
}
