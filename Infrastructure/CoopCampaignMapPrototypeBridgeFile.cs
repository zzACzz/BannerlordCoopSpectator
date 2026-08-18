using System;
using System.IO;

namespace CoopSpectator.Infrastructure
{
    public static class CoopCampaignMapPrototypeBridgeFile
    {
        private const string CoopSpectatorSubFolder = "CoopSpectator";
        private const string StateFileName = "campaign_map_prototype_host_state.txt";
        private const string CatalogFileName = "campaign_map_prototype_catalog.txt";
        private const string DynamicFileName = "campaign_map_prototype_dynamic.txt";
        private static readonly object ReadCacheLock = new object();
        private static string _cachedPath;
        private static DateTime _cachedWriteUtc = DateTime.MinValue;
        private static long _cachedLength = -1;
        private static CoopCampaignMapPrototypeHostSnapshot _cachedSnapshot;
        private static string _cachedParseReason;
        private static string _cachedCatalogPath;
        private static DateTime _cachedCatalogWriteUtc = DateTime.MinValue;
        private static long _cachedCatalogLength = -1;
        private static CoopCampaignMapPrototypeCatalogSnapshot _cachedCatalog;
        private static string _cachedCatalogParseReason;
        private static string _cachedDynamicPath;
        private static DateTime _cachedDynamicWriteUtc = DateTime.MinValue;
        private static long _cachedDynamicLength = -1;
        private static CoopCampaignMapPrototypeDynamicSnapshot _cachedDynamic;
        private static string _cachedDynamicParseReason;

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

        public static string GetCatalogFilePath()
        {
            return Path.Combine(
                Path.GetDirectoryName(GetStateFilePath()) ?? string.Empty,
                CatalogFileName);
        }

        public static string GetDynamicFilePath()
        {
            return Path.Combine(
                Path.GetDirectoryName(GetStateFilePath()) ?? string.Empty,
                DynamicFileName);
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

        public static bool TryWriteCatalog(
            CoopCampaignMapPrototypeCatalogSnapshot snapshot,
            out string reason)
        {
            return TryWriteLines(
                GetCatalogFilePath(),
                snapshot == null
                    ? null
                    : CoopCampaignMapPrototypeBridgeCodec.SerializeCatalog(snapshot),
                out reason);
        }

        public static bool TryWriteDynamic(
            CoopCampaignMapPrototypeDynamicSnapshot snapshot,
            out string reason)
        {
            return TryWriteLines(
                GetDynamicFilePath(),
                snapshot == null
                    ? null
                    : CoopCampaignMapPrototypeBridgeCodec.SerializeDynamic(snapshot),
                out reason);
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

        public static bool TryReadFreshCatalog(
            DateTime utcNow,
            TimeSpan maximumAge,
            out CoopCampaignMapPrototypeCatalogSnapshot snapshot,
            out string reason)
        {
            snapshot = null;
            reason = null;
            string path = GetCatalogFilePath();
            try
            {
                if (!TryGetFileIdentity(path, out DateTime writeUtc, out long length, out reason))
                    return false;

                CoopCampaignMapPrototypeCatalogSnapshot parsed;
                string parseReason;
                lock (ReadCacheLock)
                {
                    if (string.Equals(_cachedCatalogPath, path, StringComparison.OrdinalIgnoreCase) &&
                        _cachedCatalogWriteUtc == writeUtc &&
                        _cachedCatalogLength == length)
                    {
                        parsed = _cachedCatalog?.Clone();
                        parseReason = _cachedCatalogParseReason;
                    }
                    else
                    {
                        bool success = CoopCampaignMapPrototypeBridgeCodec.TryParseCatalog(
                            AtomicBridgeFileIO.ReadAllLinesShared(path),
                            out parsed,
                            out parseReason);
                        _cachedCatalogPath = path;
                        _cachedCatalogWriteUtc = writeUtc;
                        _cachedCatalogLength = length;
                        _cachedCatalog = success ? parsed?.Clone() : null;
                        _cachedCatalogParseReason = parseReason;
                    }
                }

                if (parsed == null ||
                    !CoopCampaignMapPrototypeContract.TryValidateCatalogSnapshot(parsed, out reason) ||
                    !IsFresh(parsed.UpdatedUtc, utcNow, maximumAge, out reason))
                {
                    reason = reason ?? parseReason ?? "malformed";
                    return false;
                }
                snapshot = parsed;
                return true;
            }
            catch (Exception ex)
            {
                reason = ex.GetType().Name + ":" + ex.Message;
                return false;
            }
        }

        public static bool TryReadFreshDynamic(
            DateTime utcNow,
            TimeSpan maximumAge,
            out CoopCampaignMapPrototypeDynamicSnapshot snapshot,
            out string reason)
        {
            snapshot = null;
            reason = null;
            string path = GetDynamicFilePath();
            try
            {
                if (!TryGetFileIdentity(path, out DateTime writeUtc, out long length, out reason))
                    return false;

                CoopCampaignMapPrototypeDynamicSnapshot parsed;
                string parseReason;
                lock (ReadCacheLock)
                {
                    if (string.Equals(_cachedDynamicPath, path, StringComparison.OrdinalIgnoreCase) &&
                        _cachedDynamicWriteUtc == writeUtc &&
                        _cachedDynamicLength == length)
                    {
                        parsed = _cachedDynamic?.Clone();
                        parseReason = _cachedDynamicParseReason;
                    }
                    else
                    {
                        bool success = CoopCampaignMapPrototypeBridgeCodec.TryParseDynamic(
                            AtomicBridgeFileIO.ReadAllLinesShared(path),
                            out parsed,
                            out parseReason);
                        _cachedDynamicPath = path;
                        _cachedDynamicWriteUtc = writeUtc;
                        _cachedDynamicLength = length;
                        _cachedDynamic = success ? parsed?.Clone() : null;
                        _cachedDynamicParseReason = parseReason;
                    }
                }

                if (parsed == null ||
                    !CoopCampaignMapPrototypeContract.TryValidateDynamicSnapshot(parsed, out reason) ||
                    !IsFresh(parsed.UpdatedUtc, utcNow, maximumAge, out reason))
                {
                    reason = reason ?? parseReason ?? "malformed";
                    return false;
                }
                snapshot = parsed;
                return true;
            }
            catch (Exception ex)
            {
                reason = ex.GetType().Name + ":" + ex.Message;
                return false;
            }
        }

        private static bool TryWriteLines(
            string path,
            string[] lines,
            out string reason)
        {
            reason = null;
            if (lines == null)
            {
                reason = "missing";
                return false;
            }
            try
            {
                AtomicBridgeFileIO.WriteAllLines(path, lines);
                return true;
            }
            catch (Exception ex)
            {
                reason = ex.GetType().Name + ":" + ex.Message;
                return false;
            }
        }

        private static bool TryGetFileIdentity(
            string path,
            out DateTime writeUtc,
            out long length,
            out string reason)
        {
            writeUtc = DateTime.MinValue;
            length = -1;
            reason = null;
            if (!File.Exists(path))
            {
                reason = "missing-file";
                return false;
            }
            writeUtc = File.GetLastWriteTimeUtc(path);
            length = new FileInfo(path).Length;
            return true;
        }

        private static bool IsFresh(
            DateTime updatedUtc,
            DateTime utcNow,
            TimeSpan maximumAge,
            out string reason)
        {
            reason = null;
            if (updatedUtc == DateTime.MinValue)
            {
                reason = "timestamp";
                return false;
            }
            DateTime now = utcNow.Kind == DateTimeKind.Utc ? utcNow : utcNow.ToUniversalTime();
            DateTime updated = updatedUtc.Kind == DateTimeKind.Utc
                ? updatedUtc
                : updatedUtc.ToUniversalTime();
            TimeSpan age = now - updated;
            if (age < TimeSpan.FromSeconds(-5d))
            {
                reason = "future";
                return false;
            }
            if (maximumAge > TimeSpan.Zero && age > maximumAge)
            {
                reason = "stale";
                return false;
            }
            return true;
        }
    }
}
