using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml;

namespace CoopSpectator.DedicatedHelper
{
    internal sealed class SharedServerModuleLease : IDisposable
    {
        private readonly FileStream _lock;
        internal string Summary { get; private set; }
        internal SharedServerModuleLease(FileStream fileLock, string summary) { _lock = fileLock; Summary = summary; }
        public void Dispose() { _lock.Dispose(); }
    }

    internal static class SharedServerModules
    {
        private sealed class Entry
        {
            internal string Name, Source, Destination, Staged, Backup;
            internal bool Reuse, OriginalMoved, Installed, BackupOnly, OwnedLink;
        }

        // Held until the caller has launched the server. All mutations are inside
        // the exact server root; source game resources are read-only to this code.
        internal static SharedServerModuleLease Prepare(string gameRoot, string serverRoot,
            Func<bool> serverIsRunning, Action<string, string> moveDirectory = null)
        {
            if (Environment.OSVersion.Platform != PlatformID.Win32NT)
                throw new IOException("Shared server modules require Windows directory junctions.");
            gameRoot = WindowsDirectoryJunction.ResolveDirectory(gameRoot);
            serverRoot = WindowsDirectoryJunction.ResolveDirectory(serverRoot);
            if (Contains(gameRoot, serverRoot) || Contains(serverRoot, gameRoot))
                throw new IOException("Game and dedicated server installations must be separate directories.");
            string sourceModules = WindowsDirectoryJunction.ResolveDirectory(Path.Combine(gameRoot, "Modules"));
            string destinationModules = Path.Combine(serverRoot, "Modules");
            if (!Directory.Exists(destinationModules) || WindowsDirectoryJunction.IsReparsePoint(destinationModules))
                throw new IOException("Dedicated Modules must be an existing ordinary directory: " + destinationModules);
            if (!Contains(serverRoot, WindowsDirectoryJunction.ResolveDirectory(destinationModules)))
                throw new IOException("Dedicated Modules resolves outside its installation.");

            var fileLock = new FileStream(Path.Combine(serverRoot, "CoopSpectator.shared-modules.lock"),
                FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            var entries = new List<Entry>();
            string transaction = null;
            try
            {
                if (serverIsRunning == null || serverIsRunning())
                    throw new IOException("Close the dedicated server before preparing shared modules.");
                foreach (string name in new[] { "SandBox", "SandBoxCore" })
                {
                    string source = WindowsDirectoryJunction.ResolveDirectory(Path.Combine(sourceModules, name));
                    if (Contains(serverRoot, source) || Contains(source, serverRoot))
                        throw new IOException("A game module resolves into the server installation: " + name);
                    ValidateSource(source, name);
                    var entry = new Entry { Name = name, Source = source, Destination = Path.Combine(destinationModules, name) };
                    if (WindowsDirectoryJunction.Exists(entry.Destination))
                    {
                        if ((File.GetAttributes(entry.Destination) & FileAttributes.Directory) == 0)
                            throw new IOException("A file occupies the server module path: " + entry.Destination);
                        if (WindowsDirectoryJunction.IsReparsePoint(entry.Destination))
                        {
                            // Unknown links and links to another game are never replaced.
                            if (!WindowsDirectoryJunction.SamePath(WindowsDirectoryJunction.ReadTarget(entry.Destination), source))
                                throw new IOException("Server module link targets another installation: " + entry.Destination);
                            if (!WindowsDirectoryJunction.SamePath(WindowsDirectoryJunction.ResolveDirectory(entry.Destination), source))
                                throw new IOException("Server module link no longer reaches its expected source: " + entry.Destination);
                            entry.Reuse = true;
                        }
                    }
                    entries.Add(entry);
                }
                PlanLegacyHideouts(serverRoot, entries[0].Source, entries);
                PlanLegacyNavalLink(serverRoot, sourceModules, entries);
                if (entries.TrueForAll(e => e.Reuse))
                    return new SharedServerModuleLease(fileLock, "SandBox and SandBoxCore already share the running game's resources.");

                string backupRoot = Path.Combine(serverRoot, "CoopSpectator.SharedModuleBackups");
                if (WindowsDirectoryJunction.Exists(backupRoot) && WindowsDirectoryJunction.IsReparsePoint(backupRoot))
                    throw new IOException("Shared-module backup directory must not be a link: " + backupRoot);
                Directory.CreateDirectory(backupRoot);
                if (!Contains(serverRoot, WindowsDirectoryJunction.ResolveDirectory(backupRoot)))
                    throw new IOException("Shared-module backup directory resolves outside the server.");
                transaction = Path.Combine(backupRoot, DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff") + "-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(transaction);
                foreach (Entry entry in entries)
                {
                    if (entry.Reuse) continue;
                    entry.Backup = Path.Combine(transaction, entry.Name);
                    if (!entry.BackupOnly)
                    {
                        entry.Staged = Path.Combine(transaction, entry.Name + ".pending");
                        WindowsDirectoryJunction.Create(entry.Staged, entry.Source);
                    }
                }
                WriteJournal(transaction, entries, "prepared");
                if (serverIsRunning()) throw new IOException("Dedicated server started while shared modules were being prepared.");
                Action<string, string> move = moveDirectory ?? Directory.Move;
                foreach (Entry entry in entries)
                {
                    if (entry.Reuse) continue;
                    if (WindowsDirectoryJunction.Exists(entry.Destination))
                    {
                        bool linked = WindowsDirectoryJunction.IsReparsePoint(entry.Destination);
                        if (entry.OwnedLink ? !linked ||
                            !WindowsDirectoryJunction.SamePath(WindowsDirectoryJunction.ReadTarget(entry.Destination), entry.Source) ||
                            !WindowsDirectoryJunction.SamePath(WindowsDirectoryJunction.ResolveDirectory(entry.Destination), entry.Source) : linked)
                            throw new IOException("Server module path changed during preparation: " + entry.Destination);
                        move(entry.Destination, entry.Backup);
                        entry.OriginalMoved = true;
                        if (entry.OwnedLink && (!WindowsDirectoryJunction.IsReparsePoint(entry.Backup) ||
                            !WindowsDirectoryJunction.SamePath(WindowsDirectoryJunction.ReadTarget(entry.Backup), entry.Source)))
                            throw new IOException("Preserved Naval link identity changed: " + entry.Backup);
                    }
                    else if (entry.OwnedLink)
                        throw new IOException("Owned Naval link disappeared during preparation.");
                    if (!entry.BackupOnly)
                    {
                        move(entry.Staged, entry.Destination);
                        entry.Installed = true;
                        if (!WindowsDirectoryJunction.SamePath(WindowsDirectoryJunction.ResolveDirectory(entry.Destination), entry.Source))
                            throw new IOException("Shared module verification failed: " + entry.Name);
                    }
                }
                WriteJournal(transaction, entries, "committed");
                return new SharedServerModuleLease(fileLock,
                    "SandBox and SandBoxCore read from " + gameRoot + ". Previous folders retained in " + transaction + ".");
            }
            catch (Exception failure)
            {
                var rollbackErrors = new List<string>();
                for (int i = entries.Count - 1; i >= 0; i--)
                {
                    Entry entry = entries[i];
                    try
                    {
                        if (entry.Installed) WindowsDirectoryJunction.RemoveExact(entry.Destination, entry.Source);
                        if (entry.OriginalMoved) Directory.Move(entry.Backup, entry.Destination);
                        if (entry.Staged != null && WindowsDirectoryJunction.Exists(entry.Staged))
                            WindowsDirectoryJunction.RemoveExact(entry.Staged, entry.Source);
                    }
                    catch (Exception rollback) { rollbackErrors.Add(entry.Name + ": " + rollback.Message); }
                }
                if (transaction != null)
                {
                    try { WriteJournal(transaction, entries, rollbackErrors.Count == 0 ? "rolled-back" : "rollback-incomplete"); }
                    catch (Exception journal) { rollbackErrors.Add("journal: " + journal.Message); }
                }
                fileLock.Dispose();
                throw new IOException("Shared server module preparation failed. " + failure.Message +
                    (rollbackErrors.Count == 0 ? "" : " Restore from " + transaction + "; " + string.Join("; ", rollbackErrors)), failure);
            }
        }

        private static bool Contains(string root, string path) =>
            WindowsDirectoryJunction.SamePath(root, path) || path.StartsWith(root.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);

        private static void PlanLegacyNavalLink(string serverRoot, string sourceModules, List<Entry> entries)
        {
            // Native scans every physical module before selecting active modules.
            // Exposing the official campaign DLC here triggers its client-only
            // base-version gate. Only our generated resource adapter belongs here.
            string destination = Path.Combine(serverRoot, "Modules", "NavalDLC");
            if (!WindowsDirectoryJunction.Exists(destination)) return;
            if (!Directory.Exists(destination) || !WindowsDirectoryJunction.IsReparsePoint(destination))
                throw new IOException("Official NavalDLC occupies the server Modules folder. Preserve/move this user-managed folder outside Modules before using the resource adapter.");
            string source = WindowsDirectoryJunction.ResolveDirectory(Path.Combine(sourceModules, "NavalDLC"));
            if (Contains(serverRoot, source) || Contains(source, serverRoot))
                throw new IOException("Naval source must remain outside the server installation.");
            ValidateSource(source, "NavalDLC");
            if (!WindowsDirectoryJunction.SamePath(WindowsDirectoryJunction.ReadTarget(destination), source) ||
                !WindowsDirectoryJunction.SamePath(WindowsDirectoryJunction.ResolveDirectory(destination), source) ||
                !HasCreatedNavalLinkJournal(serverRoot, source, destination))
                throw new IOException("Unknown NavalDLC link preserved. Only an exact journal-owned game link can be relocated.");
            entries.Add(new Entry { Name = "LegacyNavalDLCLink", Source = source, Destination = destination,
                BackupOnly = true, OwnedLink = true });
        }

        private static bool HasCreatedNavalLinkJournal(string serverRoot, string source, string destination)
        {
            string backups = Path.Combine(serverRoot, "CoopSpectator.SharedModuleBackups");
            if (!Directory.Exists(backups) || WindowsDirectoryJunction.IsReparsePoint(backups) ||
                !Contains(serverRoot, WindowsDirectoryJunction.ResolveDirectory(backups))) return false;
            string[] transactions = Directory.GetDirectories(backups);
            if (transactions.Length > 512) throw new IOException("Too many shared-module journals; inspect the existing Naval link manually.");
            string expected = "NavalDLC | GameSource=" + source + " | ServerLink=" + destination + " | PreviousFolder=none | Reused=False";
            foreach (string transaction in transactions)
            {
                if (WindowsDirectoryJunction.IsReparsePoint(transaction)) continue;
                string journal = Path.Combine(transaction, "RESTORE.txt");
                if (!File.Exists(journal) || WindowsDirectoryJunction.IsReparsePoint(journal) || new FileInfo(journal).Length > 65536) continue;
                string[] lines = File.ReadAllLines(journal);
                if (lines.Length < 3 || lines.Length > 256 || lines[0] != "CoopSpectator shared modules v1" || lines[1] != "State=committed") continue;
                foreach (string line in lines)
                    if (string.Equals(line, expected, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private static void PlanLegacyHideouts(string serverRoot, string sandboxSource, List<Entry> entries)
        {
            // Earlier deployment copied these exact stock scenes into our module,
            // where they could override a newly shared Sandbox scene. Preserve the
            // old copies outside Modules; leave every unknown/custom scene alone.
            string sceneRoot = Path.Combine(serverRoot, "Modules", "CoopSpectatorDedicated", "SceneObj");
            if (!WindowsDirectoryJunction.Exists(sceneRoot)) return;
            if (WindowsDirectoryJunction.IsReparsePoint(sceneRoot) ||
                !Contains(serverRoot, WindowsDirectoryJunction.ResolveDirectory(sceneRoot)))
                throw new IOException("Legacy hideout scene folder must remain inside the server: " + sceneRoot);
            foreach (string name in new[] { "bandit_forest", "bandit_forest_sv", "desert_hideout_002", "desert_hideout_002_sv",
                "desert_hideout_004", "desert_hideout_004_sv", "forest_hideout_003", "forest_hideout_004", "forest_hideout_004_sv",
                "hideout_steppe_001", "hideout_steppe_001_sv", "hideout_steppe_002", "hideout_steppe_002_sv",
                "mountain_hideout_002", "mountain_hideout_002_sv", "mountain_hideout_004", "mountain_hideout_004_sv",
                "sea_bandit_a", "sea_bandit_a_sv", "sea_bandit_b", "sea_bandit_b_sv", "sea_bandit_c", "sea_bandit_c_sv", "sea_bandit_d_sv" })
            {
                string destination = Path.Combine(sceneRoot, name);
                if (!WindowsDirectoryJunction.Exists(destination)) continue;
                if (!Directory.Exists(destination) || WindowsDirectoryJunction.IsReparsePoint(destination))
                    throw new IOException("Legacy hideout scene is not an ordinary directory: " + destination);
                string source = WindowsDirectoryJunction.ResolveDirectory(Path.Combine(sandboxSource, "SceneObj", name));
                entries.Add(new Entry { Name = "LegacyHideout-" + name, Source = source, Destination = destination, BackupOnly = true });
            }
        }

        private static void ValidateSource(string source, string expectedId)
        {
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 1024 * 1024 };
            var document = new XmlDocument { XmlResolver = null };
            using (var reader = XmlReader.Create(Path.Combine(source, "SubModule.xml"), settings)) document.Load(reader);
            if (!string.Equals(document.SelectSingleNode("/Module/Id/@value")?.Value, expectedId, StringComparison.OrdinalIgnoreCase) ||
                !Directory.Exists(Path.Combine(source, "ModuleData")) || !Directory.Exists(Path.Combine(source, "SceneObj")))
                throw new IOException("Invalid or incomplete game module: " + source);
        }

        private static void WriteJournal(string transaction, List<Entry> entries, string state)
        {
            var lines = new List<string> { "CoopSpectator shared modules v1", "State=" + state };
            foreach (Entry entry in entries)
                lines.Add(entry.Name + " | GameSource=" + entry.Source + " | ServerLink=" + entry.Destination +
                    " | PreviousFolder=" + (entry.OriginalMoved ? entry.Backup : "none") + " | Reused=" + entry.Reuse +
                    (entry.OwnedLink ? " | PreservedLinkTarget=" + entry.Source + " | BackupOnly=True" : ""));
            File.WriteAllLines(Path.Combine(transaction, "RESTORE.txt"), lines, new UTF8Encoding(false));
        }
    }
}
