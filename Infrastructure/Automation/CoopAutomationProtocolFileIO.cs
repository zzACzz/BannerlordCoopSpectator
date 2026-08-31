using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using Newtonsoft.Json;

namespace CoopSpectator.Infrastructure.Automation
{
    public delegate void CoopAutomationAtomicCommit(string temporaryPath, string destinationPath, bool destinationExists);

    public static class CoopAutomationProtocolFileIO
    {
        private const int AppendRetryCount = 100;
        private const int AppendRetryDelayMilliseconds = 5;

        public static void WriteJsonStrictAtomic<T>(string path, T value, CoopAutomationAtomicCommit commit = null)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Path must be provided.", nameof(path));

            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            string temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                string json = JsonConvert.SerializeObject(value, Formatting.Indented);
                File.WriteAllText(temporaryPath, json + Environment.NewLine, new UTF8Encoding(false));
                bool destinationExists = File.Exists(path);
                if (commit != null)
                    commit(temporaryPath, path, destinationExists);
                else if (destinationExists)
                    File.Replace(temporaryPath, path, null, ignoreMetadataErrors: true);
                else
                    File.Move(temporaryPath, path);
            }
            catch
            {
                TryDelete(temporaryPath);
                throw;
            }
        }

        public static bool TryReadJson<T>(string path, int maximumBytes, out T value, out string failureCode, out string failureMessage)
        {
            value = default(T);
            failureCode = string.Empty;
            failureMessage = string.Empty;
            if (string.IsNullOrWhiteSpace(path))
                return Fail("FileMissing", "The protocol file does not exist.", out failureCode, out failureMessage);

            Exception lastTransientException = null;
            for (int attempt = 0; attempt < AppendRetryCount; attempt++)
            {
                try
                {
                    string json;
                    using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    {
                        if (stream.Length <= 0)
                            return Fail("FileEmpty", "The protocol file is empty.", out failureCode, out failureMessage);
                        if (maximumBytes > 0 && stream.Length > maximumBytes)
                            return Fail("FileTooLarge", "The protocol file exceeds the configured size limit.", out failureCode, out failureMessage);
                        using (var reader = new StreamReader(stream, Encoding.UTF8, true))
                            json = reader.ReadToEnd();
                    }

                    value = JsonConvert.DeserializeObject<T>(json);
                    if (value == null)
                        return Fail("JsonNull", "The protocol JSON decoded to null.", out failureCode, out failureMessage);
                    return true;
                }
                catch (JsonException ex)
                {
                    return Fail("JsonMalformed", "The protocol JSON is malformed: " + ex.Message, out failureCode, out failureMessage);
                }
                catch (FileNotFoundException ex)
                {
                    lastTransientException = ex;
                }
                catch (DirectoryNotFoundException ex)
                {
                    lastTransientException = ex;
                }
                catch (IOException ex)
                {
                    lastTransientException = ex;
                }
                catch (UnauthorizedAccessException ex)
                {
                    lastTransientException = ex;
                }

                if (attempt + 1 < AppendRetryCount)
                    Thread.Sleep(AppendRetryDelayMilliseconds);
            }

            if (!File.Exists(path))
                return Fail("FileMissing", "The protocol file does not exist after the bounded read retry policy.", out failureCode, out failureMessage);
            return Fail("FileReadFailed", "The protocol file could not be read after bounded retries: " + (lastTransientException?.Message ?? "unknown error"), out failureCode, out failureMessage);
        }

        public static void AppendJsonLineStrict<T>(string path, T value)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Path must be provided.", nameof(path));

            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);
            byte[] bytes = new UTF8Encoding(false).GetBytes(JsonConvert.SerializeObject(value, Formatting.None) + "\n");

            for (int attempt = 0; attempt < AppendRetryCount; attempt++)
            {
                try
                {
                    using (var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read))
                    {
                        stream.Write(bytes, 0, bytes.Length);
                        stream.Flush(true);
                    }
                    return;
                }
                catch (IOException) when (attempt + 1 < AppendRetryCount)
                {
                    Thread.Sleep(AppendRetryDelayMilliseconds);
                }
                catch (UnauthorizedAccessException) when (attempt + 1 < AppendRetryCount)
                {
                    Thread.Sleep(AppendRetryDelayMilliseconds);
                }
            }

            throw new IOException("The append journal remained locked beyond the bounded retry policy.");
        }

        public static bool TryReadJsonLines<T>(string path, out List<T> records, out string failureCode, out string failureMessage)
        {
            records = new List<T>();
            failureCode = string.Empty;
            failureMessage = string.Empty;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return Fail("FileMissing", "The event journal does not exist.", out failureCode, out failureMessage);

            try
            {
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var reader = new StreamReader(stream, Encoding.UTF8, true))
                {
                    int lineNumber = 0;
                    while (!reader.EndOfStream)
                    {
                        string line = reader.ReadLine();
                        lineNumber++;
                        if (string.IsNullOrWhiteSpace(line))
                            continue;
                        T record = JsonConvert.DeserializeObject<T>(line);
                        if (record == null)
                            return Fail("JsonLineNull", "Event line " + lineNumber + " decoded to null.", out failureCode, out failureMessage);
                        records.Add(record);
                    }
                }
                return true;
            }
            catch (JsonException ex)
            {
                return Fail("JsonLineMalformed", "The event journal contains a malformed or partial line: " + ex.Message, out failureCode, out failureMessage);
            }
            catch (Exception ex)
            {
                return Fail("JournalReadFailed", "The event journal could not be read: " + ex.Message, out failureCode, out failureMessage);
            }
        }

        public static bool TryMoveInboxToProcessed(string inboxPath, string processedPath, out string failureCode, out string failureMessage)
        {
            failureCode = string.Empty;
            failureMessage = string.Empty;
            if (!File.Exists(inboxPath))
                return Fail("InboxMissing", "The inbox command does not exist.", out failureCode, out failureMessage);
            if (File.Exists(processedPath))
                return Fail("ProcessedAlreadyExists", "The processed command already exists.", out failureCode, out failureMessage);

            try
            {
                string inboxRoot = Path.GetPathRoot(Path.GetFullPath(inboxPath));
                string processedRoot = Path.GetPathRoot(Path.GetFullPath(processedPath));
                if (!string.Equals(inboxRoot, processedRoot, StringComparison.OrdinalIgnoreCase))
                    return Fail("CrossVolumeMoveRejected", "Command processing requires same-volume move semantics.", out failureCode, out failureMessage);

                string directory = Path.GetDirectoryName(processedPath);
                if (!string.IsNullOrWhiteSpace(directory))
                    Directory.CreateDirectory(directory);
                File.Move(inboxPath, processedPath);
                return true;
            }
            catch (Exception ex)
            {
                return Fail("ProcessMoveFailed", "The command could not be moved to processed state: " + ex.Message, out failureCode, out failureMessage);
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
            }
        }

        private static bool Fail(string code, string message, out string failureCode, out string failureMessage)
        {
            failureCode = code;
            failureMessage = message;
            return false;
        }
    }
}
