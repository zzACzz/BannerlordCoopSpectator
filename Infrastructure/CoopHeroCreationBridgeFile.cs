using System;
using System.IO;
using Newtonsoft.Json;

namespace CoopSpectator.Infrastructure
{
    public static class CoopHeroCreationBridgeFile
    {
        private const string RequestFileName = "hero_creation_request.json";
        private const string ResultFileName = "hero_creation_result.json";
        private const string ProgressFileName = "hero_creation_progress.json";

        public static string GetDirectoryPath()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "Mount and Blade II Bannerlord",
                "CoopSpectator");
        }

        public static string GetRequestPath() => Path.Combine(GetDirectoryPath(), RequestFileName);
        public static string GetResultPath() => Path.Combine(GetDirectoryPath(), ResultFileName);
        public static string GetProgressPath() => Path.Combine(GetDirectoryPath(), ProgressFileName);

        public static void WriteRequest(CoopHeroCreationRequest request)
        {
            AtomicBridgeFileIO.WriteAllLines(GetRequestPath(), new[] { JsonConvert.SerializeObject(request, Formatting.None) });
        }

        public static bool TryReadRequest(out CoopHeroCreationRequest request, out string error)
        {
            return TryRead(GetRequestPath(), out request, out error);
        }

        public static void WriteResult(CoopHeroCreationResult result)
        {
            AtomicBridgeFileIO.WriteAllLines(GetResultPath(), new[] { JsonConvert.SerializeObject(result, Formatting.None) });
        }

        public static bool TryReadResult(out CoopHeroCreationResult result, out string error)
        {
            return TryRead(GetResultPath(), out result, out error);
        }

        public static void WriteProgress(CoopHeroCreationProgressSnapshot progress)
        {
            AtomicBridgeFileIO.WriteAllLines(GetProgressPath(), new[] { JsonConvert.SerializeObject(progress, Formatting.None) });
        }

        public static bool TryReadProgress(out CoopHeroCreationProgressSnapshot progress, out string error)
        {
            return TryRead(GetProgressPath(), out progress, out error);
        }

        private static bool TryRead<T>(string path, out T value, out string error) where T : class
        {
            value = null;
            error = string.Empty;
            try
            {
                string[] lines = AtomicBridgeFileIO.ReadAllLinesShared(path);
                if (lines.Length == 0) { error = "file_missing_or_empty"; return false; }
                value = JsonConvert.DeserializeObject<T>(string.Join(Environment.NewLine, lines));
                if (value == null) { error = "json_deserialized_null"; return false; }
                return true;
            }
            catch (Exception ex)
            {
                error = ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }
    }
}
