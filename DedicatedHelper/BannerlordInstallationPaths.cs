using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace CoopSpectator.DedicatedHelper
{
    internal static class BannerlordInstallationPaths
    {
        internal const string ServerExecutable = "DedicatedCustomServer.Starter.exe";
        private const string ServerFolder = "Mount & Blade II Dedicated Server";

        internal static IEnumerable<string> GetSteamRoots()
        {
            var roots = new List<string>();
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
                {
                    string root = key?.GetValue("SteamPath") as string;
                    if (!string.IsNullOrWhiteSpace(root)) roots.Add(root);
                }
            }
            catch (Exception) { /* Game-library discovery and explicit path remain available. */ }
            return roots;
        }

        internal static string FindDedicatedServer(string gameRoot, string explicitPath, IEnumerable<string> steamRoots)
        {
            // An explicit override is authoritative; never launch an unrelated fallback
            // when the user supplied an invalid path.
            if (!string.IsNullOrWhiteSpace(explicitPath)) return ResolveExecutable(explicitPath);
            var libraries = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(gameRoot))
            {
                string common = Path.GetDirectoryName(Path.GetFullPath(gameRoot));
                string sibling = common == null ? null : ResolveExecutable(Path.Combine(common, ServerFolder));
                if (sibling != null) return sibling;
                var current = new DirectoryInfo(gameRoot);
                for (int depth = 0; current != null && depth < 6; depth++, current = current.Parent)
                    if (string.Equals(current.Name, "steamapps", StringComparison.OrdinalIgnoreCase))
                    { AddLibrary(current.Parent?.FullName, libraries, seen); break; }
            }
            if (steamRoots != null)
                foreach (string root in steamRoots) AddLibrary(root, libraries, seen);

            // Only Steam's configured libraries are inspected, never entire drives.
            for (int i = 0; i < libraries.Count && i < 128; i++)
            {
                string library = libraries[i];
                foreach (string file in new[] { Path.Combine(library, "steamapps", "libraryfolders.vdf"),
                    Path.Combine(library, "config", "libraryfolders.vdf") })
                    foreach (string root in ReadLibraryPaths(ReadBounded(file))) AddLibrary(root, libraries, seen);
            }
            foreach (string library in libraries)
            {
                string common = Path.Combine(library, "steamapps", "common");
                string manifest = ReadBounded(Path.Combine(library, "steamapps", "appmanifest_1863440.acf"));
                Match install = Regex.Match(manifest, "\"installdir\"\\s+\"(?<value>[^\"]+)\"", RegexOptions.IgnoreCase);
                if (install.Success)
                {
                    string folder = install.Groups["value"].Value;
                    if (folder != "." && folder != ".." && folder.IndexOfAny(new[] { '\\', '/', ':' }) < 0)
                    {
                        string found = ResolveExecutable(Path.Combine(common, folder));
                        if (found != null) return found;
                    }
                }
                string fallback = ResolveExecutable(Path.Combine(common, ServerFolder));
                if (fallback != null) return fallback;
            }
            return null;
        }

        internal static IEnumerable<string> ReadLibraryPaths(string text)
        {
            var result = new List<string>();
            // Current nested path fields and legacy numeric library entries.
            foreach (Match match in Regex.Matches(text ?? "",
                "\"(?<key>path|[0-9]+)\"\\s+\"(?<value>(?:\\\\.|[^\"\\\\])*)\"", RegexOptions.IgnoreCase))
            {
                string value = match.Groups["value"].Value.Replace(@"\\", @"\");
                if (value.Length >= 3 && char.IsLetter(value[0]) && value[1] == ':' && (value[2] == '\\' || value[2] == '/'))
                    result.Add(value);
            }
            return result;
        }

        private static string ResolveExecutable(string rootOrExecutable)
        {
            if (string.IsNullOrWhiteSpace(rootOrExecutable)) return null;
            try
            {
                string input = rootOrExecutable.Trim().Trim('"');
                if (input.Length < 3 || !char.IsLetter(input[0]) || input[1] != ':' || (input[2] != '\\' && input[2] != '/')) return null;
                string path = Path.GetFullPath(input);
                if (File.Exists(path))
                    return string.Equals(Path.GetFileName(path), ServerExecutable, StringComparison.OrdinalIgnoreCase) ? path : null;
                path = Path.Combine(path, "bin", "Win64_Shipping_Server", ServerExecutable);
                return File.Exists(path) ? path : null;
            }
            catch (Exception) { return null; }
        }

        private static void AddLibrary(string path, List<string> roots, HashSet<string> seen)
        {
            if (string.IsNullOrWhiteSpace(path) || roots.Count >= 128) return;
            try
            {
                path = Path.GetFullPath(path.Trim().Trim('"')).TrimEnd('\\', '/');
                if (Directory.Exists(path) && seen.Add(path)) roots.Add(path);
            }
            catch (Exception) { }
        }

        private static string ReadBounded(string path)
        {
            try
            {
                var file = new FileInfo(path);
                return file.Exists && file.Length <= 4 * 1024 * 1024 ? File.ReadAllText(path) : "";
            }
            catch (Exception) { return ""; }
        }
    }
}
