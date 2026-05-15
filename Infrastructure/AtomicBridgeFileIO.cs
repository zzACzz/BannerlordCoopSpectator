using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace CoopSpectator.Infrastructure
{
    internal static class AtomicBridgeFileIO
    {
        private const int SharedIoRetryCount = 4;
        private const int SharedIoRetryDelayMilliseconds = 5;

        public static void WriteAllLines(string path, IEnumerable<string> lines)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Path must be provided.", nameof(path));

            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            string[] materializedLines = MaterializeLines(lines);
            string tempPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllLines(tempPath, materializedLines);
                if (File.Exists(path))
                {
                    try
                    {
                        File.Replace(tempPath, path, null, ignoreMetadataErrors: true);
                    }
                    catch (IOException)
                    {
                        WriteAllLinesDirectShared(path, materializedLines);
                        TryDeleteTempFile(tempPath);
                    }
                    catch (UnauthorizedAccessException)
                    {
                        WriteAllLinesDirectShared(path, materializedLines);
                        TryDeleteTempFile(tempPath);
                    }
                }
                else
                {
                    try
                    {
                        File.Move(tempPath, path);
                    }
                    catch (IOException)
                    {
                        WriteAllLinesDirectShared(path, materializedLines);
                        TryDeleteTempFile(tempPath);
                    }
                    catch (UnauthorizedAccessException)
                    {
                        WriteAllLinesDirectShared(path, materializedLines);
                        TryDeleteTempFile(tempPath);
                    }
                }
            }
            catch
            {
                TryDeleteTempFile(tempPath);
                throw;
            }
        }

        public static string[] ReadAllLinesShared(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return Array.Empty<string>();

            for (int attempt = 0; attempt < SharedIoRetryCount; attempt++)
            {
                try
                {
                    using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    using (StreamReader reader = new StreamReader(stream))
                    {
                        List<string> lines = new List<string>();
                        while (!reader.EndOfStream)
                            lines.Add(reader.ReadLine());
                        return lines.ToArray();
                    }
                }
                catch (IOException) when (attempt + 1 < SharedIoRetryCount)
                {
                    Thread.Sleep(SharedIoRetryDelayMilliseconds);
                }
                catch (UnauthorizedAccessException) when (attempt + 1 < SharedIoRetryCount)
                {
                    Thread.Sleep(SharedIoRetryDelayMilliseconds);
                }
            }

            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (StreamReader reader = new StreamReader(stream))
            {
                List<string> lines = new List<string>();
                while (!reader.EndOfStream)
                    lines.Add(reader.ReadLine());
                return lines.ToArray();
            }
        }

        private static string[] MaterializeLines(IEnumerable<string> lines)
        {
            if (lines == null)
                return Array.Empty<string>();

            return lines as string[] ?? new List<string>(lines).ToArray();
        }

        private static void WriteAllLinesDirectShared(string path, IEnumerable<string> lines)
        {
            using (FileStream stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
            using (StreamWriter writer = new StreamWriter(stream))
            {
                foreach (string line in lines ?? Array.Empty<string>())
                    writer.WriteLine(line ?? string.Empty);

                writer.Flush();
                stream.Flush();
            }
        }

        private static void TryDeleteTempFile(string tempPath)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(tempPath) && File.Exists(tempPath))
                    File.Delete(tempPath);
            }
            catch
            {
                // Best-effort cleanup only.
            }
        }
    }
}
