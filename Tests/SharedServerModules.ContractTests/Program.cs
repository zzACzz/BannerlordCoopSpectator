using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using CoopSpectator.DedicatedHelper;

internal static class Program
{
    private static int Main()
    {
        try
        {
            if (!OperatingSystem.IsWindows()) throw new InvalidOperationException("Real junction tests require Windows; do not report a skipped filesystem test as passed.");
            TestDiscovery();
            NavalResourceTests.Run();
            NavalClientModulePolicyTests.Run();
            NavalTextureAdmissionTests.Run();
            NavalMaterialCatalogTests.Run();
            BattleContentTransportTests.Run();
            ContentOwnerReadinessTests.Run();
            TestFreshAndReuse();
            TestMigration();
            TestRollback();
            TestBadSourceAndForeignLink();
            TestBusyAndOverlappingRoots();
            TestLegacyNavalLinkIsolation();
            TestBuildAndPackaging();
            Console.WriteLine("PASS shared server modules: real Windows junctions, source preservation, migration/rollback, locks, path discovery and no-copy build/package guards; no game started.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    private static void TestDiscovery()
    {
        string[] parsed = BannerlordInstallationPaths.ReadLibraryPaths(
            "\"libraryfolders\" { \"0\" { \"path\" \"D:\\\\Steam Library\" \"apps\" { \"1863440\" \"12345\" } } \"1\" \"E:\\\\Ігри\" }").ToArray();
        Check(parsed.SequenceEqual(new[] { @"D:\Steam Library", @"E:\Ігри" }), "Modern/legacy VDF paths, without treating app sizes as directories");
        WithFixture((root, game, server) =>
        {
            string steam = Path.Combine(root, "Steam elsewhere");
            string library = Path.Combine(root, "Інша бібліотека");
            Directory.CreateDirectory(Path.Combine(steam, "steamapps"));
            Directory.CreateDirectory(Path.Combine(library, "steamapps"));
            File.WriteAllText(Path.Combine(steam, "steamapps", "libraryfolders.vdf"),
                "\"libraryfolders\" { \"1\" { \"path\" \"" + library.Replace("\\", "\\\\") + "\" } }");
            string installation = Path.Combine(library, "steamapps", "common", "Dedicated custom name");
            string executable = Path.Combine(installation, "bin", "Win64_Shipping_Server", BannerlordInstallationPaths.ServerExecutable);
            Directory.CreateDirectory(Path.GetDirectoryName(executable)); File.WriteAllText(executable, "fixture");
            File.WriteAllText(Path.Combine(library, "steamapps", "appmanifest_1863440.acf"), "\"AppState\" { \"installdir\" \"Dedicated custom name\" }");
            Check(BannerlordInstallationPaths.FindDedicatedServer(game, null, new[] { steam }) == executable, "Another Steam library and manifest install directory");
            Check(BannerlordInstallationPaths.FindDedicatedServer(game, "\"" + installation + "\"", null) == executable, "Quoted explicit installation");
            Check(BannerlordInstallationPaths.FindDedicatedServer(game, executable, null) == executable, "Explicit executable");
            Check(BannerlordInstallationPaths.FindDedicatedServer(game, Path.Combine(root, "missing"), new[] { steam }) == null, "Invalid explicit override must not silently select another server");
            Check(BannerlordInstallationPaths.FindDedicatedServer(game, ".", new[] { steam }) == null, "Relative explicit paths must not depend on the launch working directory");
        });
    }

    private static void TestFreshAndReuse()
    {
        WithFixture((root, game, server) =>
        {
            string original = Fingerprint(game);
            using (SharedServerModules.Prepare(game, server, () => false))
            {
                CheckShared(game, server);
                ExpectFailure(() => SharedServerModules.Prepare(game, server, () => false), "Concurrent preparation must be locked");
            }
            int backups = Directory.GetDirectories(Path.Combine(server, "CoopSpectator.SharedModuleBackups")).Length;
            using (SharedServerModules.Prepare(game, server, () => false)) CheckShared(game, server);
            Check(backups == Directory.GetDirectories(Path.Combine(server, "CoopSpectator.SharedModuleBackups")).Length, "Repeated launch must not create another migration");
            Check(Fingerprint(game) == original, "Preparing/reusing modules must not modify any source file");
        });
    }

    private static void TestMigration()
    {
        WithFixture((root, game, server) =>
        {
            AddOldCopies(server);
            string legacy = Path.Combine(server, "Modules", "CoopSpectatorDedicated", "SceneObj", "bandit_forest_sv");
            Directory.CreateDirectory(legacy); File.WriteAllText(Path.Combine(legacy, "scene.txt"), "old hideout");
            string custom = Path.Combine(server, "Modules", "CoopSpectatorDedicated", "SceneObj", "custom_scene");
            Directory.CreateDirectory(custom); File.WriteAllText(Path.Combine(custom, "custom.txt"), "custom content");
            string original = Fingerprint(game);
            using (SharedServerModules.Prepare(game, server, () => false)) CheckShared(game, server);
            string transaction = Directory.GetDirectories(Path.Combine(server, "CoopSpectator.SharedModuleBackups")).Single();
            Check(File.ReadAllText(Path.Combine(transaction, "SandBox", "old.txt")) == "old-SandBox", "Original SandBox folder preserved");
            Check(File.ReadAllText(Path.Combine(transaction, "SandBoxCore", "old.txt")) == "old-SandBoxCore", "Original SandBoxCore folder preserved");
            Check(File.ReadAllText(Path.Combine(transaction, "RESTORE.txt")).Contains("State=committed"), "Restoration locations recorded");
            Check(File.ReadAllText(Path.Combine(transaction, "LegacyHideout-bandit_forest_sv", "scene.txt")) == "old hideout", "Old hideout override preserved outside active Modules");
            Check(!Directory.Exists(legacy) && File.ReadAllText(Path.Combine(custom, "custom.txt")) == "custom content", "Stock hideout override removed, unknown scene preserved");
            Check(Fingerprint(game) == original, "Migration leaves the source game intact");
            Check(File.ReadAllText(Path.Combine(server, "Modules", "unrelated.txt")) == "untouched", "Unrelated server files preserved");
        });
    }

    private static void TestRollback()
    {
        WithFixture((root, game, server) =>
        {
            AddOldCopies(server);
            string before = Fingerprint(Path.Combine(server, "Modules"));
            ExpectFailure(() => SharedServerModules.Prepare(game, server, () => false, (source, destination) =>
            {
                if (source.EndsWith("SandBoxCore.pending", StringComparison.OrdinalIgnoreCase)) throw new IOException("Injected second install failure");
                Directory.Move(source, destination);
            }), "Second module install failure");
            Check(Fingerprint(Path.Combine(server, "Modules")) == before, "Rollback restores both original module trees exactly");
            Check(!WindowsDirectoryJunction.IsReparsePoint(Path.Combine(server, "Modules", "SandBox")), "First installed junction was removed during rollback");
            string transaction = Directory.GetDirectories(Path.Combine(server, "CoopSpectator.SharedModuleBackups")).Single();
            Check(File.ReadAllText(Path.Combine(transaction, "RESTORE.txt")).Contains("State=rolled-back"), "Rollback state recorded");
        });
    }

    private static void TestBadSourceAndForeignLink()
    {
        WithFixture((root, game, server) =>
        {
            AddOldCopies(server);
            File.Delete(Path.Combine(game, "Modules", "SandBoxCore", "SubModule.xml"));
            string before = Fingerprint(Path.Combine(server, "Modules"));
            ExpectFailure(() => SharedServerModules.Prepare(game, server, () => false), "Missing second source descriptor");
            Check(Fingerprint(Path.Combine(server, "Modules")) == before, "Validate every source before moving either original");
        });
        WithFixture((root, game, server) =>
        {
            string foreign = Path.Combine(root, "another module"); Directory.CreateDirectory(foreign);
            string link = Path.Combine(server, "Modules", "SandBoxCore"); WindowsDirectoryJunction.Create(link, foreign);
            ExpectFailure(() => SharedServerModules.Prepare(game, server, () => false), "Foreign directory junction");
            Check(WindowsDirectoryJunction.SamePath(WindowsDirectoryJunction.ReadTarget(link), foreign), "Foreign link preserved");
            Check(!Directory.Exists(Path.Combine(server, "Modules", "SandBox")), "Foreign second link must not partially migrate the first module");
        });
    }

    private static void TestBusyAndOverlappingRoots()
    {
        WithFixture((root, game, server) =>
        {
            AddOldCopies(server);
            string before = Fingerprint(Path.Combine(server, "Modules"));
            ExpectFailure(() => SharedServerModules.Prepare(game, server, () => true), "Running server");
            int calls = 0;
            ExpectFailure(() => SharedServerModules.Prepare(game, server, () => ++calls > 1), "Server starts during staging");
            Check(Fingerprint(Path.Combine(server, "Modules")) == before, "Busy guard preserves module folders");
            ExpectFailure(() => SharedServerModules.Prepare(game, game, () => false), "Same installation roots");
            string nested = Path.Combine(game, "server"); Directory.CreateDirectory(nested);
            ExpectFailure(() => SharedServerModules.Prepare(game, nested, () => false), "Nested installation roots");
        });
    }

    private static void TestLegacyNavalLinkIsolation()
    {
        WithFixture((root, game, server) =>
        {
            string source = MakeNavalSource(game);
            string destination = Path.Combine(server, "Modules", "NavalDLC");
            using (SharedServerModules.Prepare(game, server, () => false)) { }
            Check(!Directory.Exists(destination), "DLC installed in game never creates an official server module link");
            WindowsDirectoryJunction.Create(destination, source);
            ExpectFailure(() => SharedServerModules.Prepare(game, server, () => false), "Exact target without ownership journal remains foreign");
            Check(WindowsDirectoryJunction.IsReparsePoint(destination), "Unowned matching link is preserved");
            AddCreatedNavalJournal(server, source, destination);
            string before = Fingerprint(source);
            using (SharedServerModules.Prepare(game, server, () => false)) { }
            Check(!WindowsDirectoryJunction.Exists(destination), "Journal-owned official module leaves native scanner visibility");
            string[] preserved = Directory.GetDirectories(Path.Combine(server, "CoopSpectator.SharedModuleBackups"))
                .Select(p => Path.Combine(p, "LegacyNavalDLCLink")).Where(Directory.Exists).ToArray();
            Check(preserved.Length == 1 && WindowsDirectoryJunction.IsReparsePoint(preserved[0]) &&
                WindowsDirectoryJunction.SamePath(WindowsDirectoryJunction.ReadTarget(preserved[0]), source),
                "Migration moves only the exact junction and retains its target for recovery");
            Check(Fingerprint(source) == before, "Naval resource source remains byte-for-byte unchanged");
            int count = Directory.GetDirectories(Path.Combine(server, "CoopSpectator.SharedModuleBackups")).Length;
            using (SharedServerModules.Prepare(game, server, () => false)) { }
            Check(count == Directory.GetDirectories(Path.Combine(server, "CoopSpectator.SharedModuleBackups")).Length &&
                !Directory.Exists(destination), "Repeated preparation neither recreates the official link nor repeats migration");
        });
        WithFixture((root, game, server) =>
        {
            string source = MakeNavalSource(game);
            string destination = Path.Combine(server, "Modules", "NavalDLC");
            Directory.CreateDirectory(destination); File.WriteAllText(Path.Combine(destination, "user.txt"), "preserve");
            AddCreatedNavalJournal(server, source, destination);
            ExpectFailure(() => SharedServerModules.Prepare(game, server, () => false), "Ordinary Naval folder is never adopted even with a journal");
            Check(File.ReadAllText(Path.Combine(destination, "user.txt")) == "preserve", "User-managed folder untouched");
        });
        WithFixture((root, game, server) =>
        {
            string source = MakeNavalSource(game);
            string destination = Path.Combine(server, "Modules", "NavalDLC");
            string other = Path.Combine(root,"Another game's DLC"); Directory.CreateDirectory(other);
            WindowsDirectoryJunction.Create(destination,other);
            AddCreatedNavalJournal(server, source, destination);
            ExpectFailure(() => SharedServerModules.Prepare(game, server, () => false), "Foreign target rejected despite matching historical journal");
            Check(WindowsDirectoryJunction.SamePath(WindowsDirectoryJunction.ReadTarget(destination),other), "Foreign junction preserved");
        });
        WithFixture((root, game, server) =>
        {
            string source = MakeNavalSource(game);
            string destination = Path.Combine(server, "Modules", "NavalDLC");
            WindowsDirectoryJunction.Create(destination,source); AddCreatedNavalJournal(server, source, destination);
            AddOldCopies(server);
            ExpectFailure(() => SharedServerModules.Prepare(game, server, () => false, (from,to) => {
                if (WindowsDirectoryJunction.SamePath(from,destination)) throw new IOException("Injected relocation failure");
                Directory.Move(from,to);
            }), "Naval move failure rolls back previously migrated base entries");
            Check(WindowsDirectoryJunction.IsReparsePoint(destination) &&
                WindowsDirectoryJunction.SamePath(WindowsDirectoryJunction.ReadTarget(destination),source), "Original Naval link retained after failure");
            Check(File.Exists(Path.Combine(server,"Modules","SandBox","old.txt")) &&
                File.Exists(Path.Combine(server,"Modules","SandBoxCore","old.txt")), "Previous base folders restored on migration failure");
        });
        Console.WriteLine("PASS official Naval module isolation: exact journal-owned link relocation, source preservation, repeat launch, foreign/ordinary refusal and transaction rollback.");
    }

    private static string MakeNavalSource(string game)
    {
        string source = Path.Combine(game,"Modules","NavalDLC");
        Directory.CreateDirectory(Path.Combine(source,"ModuleData")); Directory.CreateDirectory(Path.Combine(source,"SceneObj"));
        File.WriteAllText(Path.Combine(source,"SubModule.xml"),"<Module><Id value='NavalDLC'/></Module>");
        File.WriteAllText(Path.Combine(source,"SceneObj","scene.txt"),"original DLC resource");
        return source;
    }

    private static void AddCreatedNavalJournal(string server, string source, string destination)
    {
        string transaction = Path.Combine(server,"CoopSpectator.SharedModuleBackups","prior-owned-transaction");
        Directory.CreateDirectory(transaction);
        File.WriteAllLines(Path.Combine(transaction,"RESTORE.txt"), new[] {
            "CoopSpectator shared modules v1", "State=committed",
            "NavalDLC | GameSource=" + source + " | ServerLink=" + destination + " | PreviousFolder=none | Reused=False" });
    }

    private static void TestBuildAndPackaging()
    {
        string repository = Environment.GetEnvironmentVariable("COOPSPECTATOR_REPOSITORY_ROOT");
        if (string.IsNullOrEmpty(repository)) throw new InvalidOperationException("Runner repository root is required.");
        var project = XDocument.Load(Path.Combine(repository, "DedicatedServer", "CoopSpectatorDedicated.csproj"));
        var deploy = project.Descendants("Target").Single(e => (string)e.Attribute("Name") == "DeployServerToDedicated");
        Check(!deploy.ToString().Contains("_DedicatedSandbox") && !deploy.ToString().Contains("_DedicatedHideoutSceneFiles"), "Deployment must not recreate base-module or hideout resource copies");
        Check(project.Descendants("Target").Any(e => (string)e.Attribute("Name") == "StageDedicatedSandBoxSceneScriptRuntime"), "Required small server scene-script dependencies remain staged");
        string package = File.ReadAllText(Path.Combine(repository, "scripts", "CreateReleasePackage.ps1"));
        Check(!package.Contains("$hostSandboxDir") && !package.Contains("$hostSandboxCoreDir"), "Packages must not embed machine resources");
    }

    private static void WithFixture(Action<string, string, string> test)
    {
        string temp = Path.GetFullPath(Path.GetTempPath());
        string root = Path.Combine(temp, "CoopSharedModulesTests-" + Guid.NewGuid().ToString("N"));
        string game = Path.Combine(root, "Ігри game"), server = Path.Combine(root, "Dedicated elsewhere");
        foreach (string name in new[] { "SandBox", "SandBoxCore" })
        {
            string module = Path.Combine(game, "Modules", name);
            Directory.CreateDirectory(Path.Combine(module, "SceneObj"));
            Directory.CreateDirectory(Path.Combine(module, "ModuleData"));
            File.WriteAllText(Path.Combine(module, "SubModule.xml"), "<Module><Id value=\"" + name + "\" /></Module>");
            File.WriteAllText(Path.Combine(module, "SceneObj", "scene.txt"), "source-" + name);
            if (name == "SandBox")
            {
                Directory.CreateDirectory(Path.Combine(module, "SceneObj", "bandit_forest_sv"));
                File.WriteAllText(Path.Combine(module, "SceneObj", "bandit_forest_sv", "scene.txt"), "original hideout");
            }
        }
        Directory.CreateDirectory(Path.Combine(server, "Modules"));
        File.WriteAllText(Path.Combine(server, "Modules", "unrelated.txt"), "untouched");
        try { test(root, game, server); }
        finally
        {
            // Remove only fixture-owned junctions before recursive scratch cleanup.
            string absolute = Path.GetFullPath(root);
            Check(absolute.StartsWith(temp.TrimEnd('\\') + "\\CoopSharedModulesTests-", StringComparison.OrdinalIgnoreCase), "Cleanup remains inside the exact fixture root");
            RemoveFixtureLinks(absolute, absolute);
            Directory.Delete(absolute, true);
        }
    }

    private static void RemoveFixtureLinks(string directory, string root)
    {
        foreach (string child in Directory.GetDirectories(directory))
        {
            if (WindowsDirectoryJunction.IsReparsePoint(child))
            {
                string target = WindowsDirectoryJunction.ReadTarget(child);
                Check(target.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase), "Only scratch-target links may be removed");
                WindowsDirectoryJunction.RemoveExact(child, target);
            }
            else RemoveFixtureLinks(child, root);
        }
    }

    private static void AddOldCopies(string server)
    {
        foreach (string name in new[] { "SandBox", "SandBoxCore" })
        { string folder = Path.Combine(server, "Modules", name); Directory.CreateDirectory(folder); File.WriteAllText(Path.Combine(folder, "old.txt"), "old-" + name); }
    }

    private static void CheckShared(string game, string server)
    {
        foreach (string name in new[] { "SandBox", "SandBoxCore" })
        {
            string link = Path.Combine(server, "Modules", name);
            Check(WindowsDirectoryJunction.IsReparsePoint(link), "Destination is a link, not a copy");
            Check(WindowsDirectoryJunction.SamePath(WindowsDirectoryJunction.ReadTarget(link), Path.Combine(game, "Modules", name)), "Link points to the exact game module");
            Check(File.ReadAllText(Path.Combine(link, "SceneObj", "scene.txt")) == "source-" + name, "Scene data is readable through the server path");
        }
    }

    private static string Fingerprint(string directory) => string.Join("|", Directory.GetFiles(directory, "*", SearchOption.AllDirectories)
        .OrderBy(p => p, StringComparer.Ordinal).Select(p => p.Substring(directory.Length) + ":" + Convert.ToBase64String(File.ReadAllBytes(p))));
    private static void ExpectFailure(Func<SharedServerModuleLease> action, string message)
    { try { using (action()) { } } catch (IOException) { return; } throw new InvalidOperationException("Expected rejection: " + message); }
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
