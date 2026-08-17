using System;
using System.IO;

namespace CoopSpectator.Infrastructure
{
    public static class CoopCampaignMapPrototypeBridgeFile
    {
        private const string CoopSpectatorSubFolder = "CoopSpectator";
        private const string StateFileName = "campaign_map_prototype_host_state.txt";
        private static readonly object ReadCacheLock = new object();
        private static string _cachedPath;
        private static DateTime _cachedWriteUtc = DateTime.MinValue;
        private static long _cachedLength = -1;
        private static CoopCampaignMapPrototypeHostSnapshot _cachedSnapshot;
        private static string _cachedParseReason;

        public static string GetStateFilePath()
        {
            string documents = Environment.GetFolderPath(
                Environment.SpecialFolder.MyDocuments);
            return Path.Combine(
                documents,
                "Mount and Blade II Bannerlord",
                CoopSpectatorSubFolder,
                StateFileName);
        }

        public static bool TryWrite(
            CoopCampaignMapPrototypeHostSnapshot snapshot,
            out string reason)
        {
            reason = null;
            if (snapshot == null)
            {
                reason = "missing";
                return false;
            }

            try
            {
                AtomicBridgeFileIO.WriteAllLines(
                    GetStateFilePath(),
                    CoopCampaignMapPrototypeBridgeCodec.Serialize(snapshot));
                return true;
            }
            catch (Exception ex)
            {
                reason = ex.GetType().Name + ":" + ex.Message;
                return false;
            }
        }

        public static bool TryReadFresh(
            DateTime utcNow,
            TimeSpan maximumAge,
            out CoopCampaignMapPrototypeHostSnapshot snapshot,
            out string reason)
        {
            snapshot = null;
            reason = null;
            string path = GetStateFilePath();
            try
            {
                if (!File.Exists(path))
                {
                    reason = "missing-file";
                    return false;
                }

                DateTime writeUtc = File.GetLastWriteTimeUtc(path);
                long length = new FileInfo(path).Length;
                CoopCampaignMapPrototypeHostSnapshot parsedSnapshot;
                string parseReason;
                lock (ReadCacheLock)
                {
                    if (string.Equals(
                            _cachedPath,
                            path,
                            StringComparison.OrdinalIgnoreCase) &&
                        _cachedWriteUtc == writeUtc &&
                        _cachedLength == length)
                    {
                        parsedSnapshot = _cachedSnapshot?.Clone();
                        parseReason = _cachedParseReason;
                    }
                    else
                    {
                        string[] lines = AtomicBridgeFileIO.ReadAllLinesShared(path);
                        bool parsed = CoopCampaignMapPrototypeBridgeCodec.TryParse(
                            lines,
                            out parsedSnapshot,
                            out parseReason);
                        _cachedPath = path;
                        _cachedWriteUtc = writeUtc;
                        _cachedLength = length;
                        _cachedSnapshot = parsed ? parsedSnapshot?.Clone() : null;
                        _cachedParseReason = parseReason;
                    }
                }

                if (parsedSnapshot == null)
                {
                    reason = parseReason ?? "malformed";
                    return false;
                }

                if (!CoopCampaignMapPrototypeContract.TryValidateHostSnapshot(
                        parsedSnapshot,
                        utcNow,
                        maximumAge,
                        out reason))
                {
                    return false;
                }

                snapshot = parsedSnapshot;
                return true;
            }
            catch (Exception ex)
            {
                reason = ex.GetType().Name + ":" + ex.Message;
                return false;
            }
        }
    }
}
