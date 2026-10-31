using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using CoopSpectator.Infrastructure;

namespace CoopSpectator.DedicatedHelper
{
    // Generates a resource-only optional module. Official SubModule.xml and bin
    // stay untouched; no Naval campaign/UI submodule is instantiated in MP/DS.
    public static class CoopNavalResourceAdapter
    {
        private static readonly string[] ResourceDirectories = { "AssetPackages", "Atmospheres", "GUI",
            "Music", "NavMeshPrefabs", "Prefabs", "SceneObj", "Sounds", "TileSets", "Videos" };
        private static readonly HashSet<string> CatalogKinds = new HashSet<string>(StringComparer.Ordinal) {
            "Items", "CraftingPieces", "CraftingTemplates", "WeaponDescriptions", "SPCultures" };

        public static string Prepare(string gameRoot, string installationRoot)
            => PrepareCore(gameRoot, installationRoot, false);

        public static string PrepareClient(string gameRoot, string installationRoot)
            => PrepareCore(gameRoot, installationRoot, true);

        internal static void ValidateClientCatalog(string gameRoot, string catalogRoot)
        {
            string source = WindowsDirectoryJunction.ResolveDirectory(Path.Combine(gameRoot, "Modules", "NavalDLC"));
            ValidateOwned(catalogRoot, source, true);
            if (File.ReadAllLines(Path.Combine(catalogRoot, "warhost-resources.txt"))[2] != CoopCampaignContentProfile.Compute(gameRoot, true))
                throw new IOException("Naval client catalog does not match the current official resources.");
        }

        private static string PrepareCore(string gameRoot, string installationRoot, bool catalogOnly)
        {
            gameRoot = WindowsDirectoryJunction.ResolveDirectory(gameRoot);
            installationRoot = WindowsDirectoryJunction.ResolveDirectory(installationRoot);
            string modules = Path.Combine(installationRoot, "Modules");
            if (!Directory.Exists(modules) || WindowsDirectoryJunction.IsReparsePoint(modules))
                throw new IOException("Module adapter requires an ordinary Modules directory.");
            string source = WindowsDirectoryJunction.ResolveDirectory(Path.Combine(gameRoot, "Modules", "NavalDLC"));
            string profile = CoopCampaignContentProfile.Compute(gameRoot, true);
            string adapterId = catalogOnly ? CoopCampaignContentProfile.NavalClientCatalogId : CoopCampaignContentProfile.NavalAdapterId;
            string target = Path.Combine(modules, adapterId);
            using (var fileLock = new FileStream(Path.Combine(installationRoot, "CoopSpectator.naval-resources.lock"),
                FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
            {
                if (WindowsDirectoryJunction.Exists(target))
                {
                    ValidateOwned(target, source, catalogOnly);
                    string[] marker = File.ReadAllLines(Path.Combine(target, "warhost-resources.txt"));
                    if (marker[2] == profile) return profile;
                }
                string backupRoot = Path.Combine(installationRoot, "CoopSpectator.SharedModuleBackups");
                if (WindowsDirectoryJunction.Exists(backupRoot) && WindowsDirectoryJunction.IsReparsePoint(backupRoot))
                    throw new IOException("Adapter backups must not use an unknown link.");
                Directory.CreateDirectory(backupRoot);
                string transaction = Path.Combine(backupRoot, "Naval-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff") + "-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(transaction);
                string staged = Path.Combine(transaction, adapterId + ".pending");
                string prior = Path.Combine(transaction, "previous-adapter");
                Directory.CreateDirectory(staged);
                bool moved = false, installed = false;
                try
                {
                    Build(staged, source, profile, catalogOnly);
                    ValidateOwned(staged, source, catalogOnly);
                    if (Directory.Exists(target)) { Directory.Move(target, prior); moved = true; }
                    Directory.Move(staged, target);
                    installed = true;
                    File.WriteAllText(Path.Combine(transaction, "RESTORE.txt"), "State=committed\nAdapter=" + target +
                        "\nSource=" + source + "\nPrevious=" + (moved ? prior : "none") + "\n", new UTF8Encoding(false));
                    return profile;
                }
                catch
                {
                    // Never recursively delete linked resources, even on failure.
                    // Staging stays outside Modules for diagnosis/manual recovery.
                    if (installed) { ValidateOwned(target, source, catalogOnly); Directory.Move(target, staged); }
                    if (moved && !WindowsDirectoryJunction.Exists(target)) Directory.Move(prior, target);
                    throw;
                }
            }
        }

        private static void Build(string staged, string source, string profile, bool catalogOnly)
        {
            foreach (string name in catalogOnly ? new string[0] : ResourceDirectories)
            {
                string directory = Path.Combine(source, name);
                if (Directory.Exists(directory)) WindowsDirectoryJunction.Create(Path.Combine(staged, name), directory);
            }
            foreach (string required in catalogOnly ? new string[0] : new[] { "AssetPackages", "SceneObj" })
                if (!Directory.Exists(Path.Combine(staged, required))) throw new IOException("Naval resources are incomplete: " + required);
            string sourceData = Path.Combine(source, "ModuleData"), localData = Path.Combine(staged, "ModuleData");
            Directory.CreateDirectory(localData);
            string[] metadata = Directory.GetFiles(sourceData, "*", SearchOption.TopDirectoryOnly);
            if (metadata.Length > 128 || metadata.Sum(p => new FileInfo(p).Length) > 16L * 1024 * 1024)
                throw new IOException("Naval metadata exceeds the bounded local projection budget.");
            if (!catalogOnly) foreach (string file in metadata) File.Copy(file, Path.Combine(localData, Path.GetFileName(file)));
            foreach (string directory in catalogOnly ? new string[0] : Directory.GetDirectories(sourceData))
            {
                if (Path.GetFileName(directory).Equals("WarhostPrepared", StringComparison.OrdinalIgnoreCase))
                    throw new IOException("Official module occupies the adapter's private metadata namespace.");
                WindowsDirectoryJunction.Create(Path.Combine(localData, Path.GetFileName(directory)), directory);
            }
            string prepared = Path.Combine(localData, "WarhostPrepared");
            Directory.CreateDirectory(Path.Combine(prepared, "XSLT"));
            XmlDocument official = CoopCampaignContentProfile.ReadDescriptor(source, "NavalDLC");
            var descriptor = new XmlDocument { XmlResolver = null };
            descriptor.LoadXml("<Module><Name value='Warhost Naval resources'/><Id value='" +
                (catalogOnly ? CoopCampaignContentProfile.NavalClientCatalogId : CoopCampaignContentProfile.NavalAdapterId) +
                "'/><Version value='v1.0.0'/><SingleplayerModule value='false'/><MultiplayerModule value='true'/>" +
                "<DependedModules><DependedModule Id='Native'/><DependedModule Id='SandBoxCore'/><DependedModule Id='Sandbox'/>" +
                "<DependedModule Id='Multiplayer'/></DependedModules><SubModules/><Xmls/></Module>");
            int index = 0;
            foreach (XmlNode entry in official.SelectNodes("/Module/Xmls/XmlNode"))
            {
                string kind = entry.SelectSingleNode("XmlName/@id")?.Value;
                if (!CatalogKinds.Contains(kind ?? string.Empty)) continue;
                foreach (string file in CoopCampaignContentProfile.ResolveDataFiles(source, entry.SelectSingleNode("XmlName/@path")?.Value))
                {
                    bool stylesheet = string.Equals(Path.GetExtension(file), ".xslt", StringComparison.OrdinalIgnoreCase);
                    string name = (stylesheet ? "XSLT/" : "") + "catalog-" + index++;
                    string output = Path.Combine(prepared, name.Replace('/', Path.DirectorySeparatorChar) + (stylesheet ? ".xslt" : ".xml"));
                    XmlDocument data = CoopCampaignContentProfile.ReadXml(file);
                    if (kind == "Items" && !stylesheet)
                        foreach (XmlElement item in data.SelectNodes("/Items/*")) item.SetAttribute("multiplayer_item", "true");
                    data.Save(output);
                    var xmlNode = descriptor.CreateElement("XmlNode");
                    var xmlName = descriptor.CreateElement("XmlName");
                    xmlName.SetAttribute("id", kind); xmlName.SetAttribute("path", "WarhostPrepared/" + name);
                    xmlNode.AppendChild(xmlName);
                    var types = descriptor.CreateElement("IncludedGameTypes");
                    foreach (string gameType in new[] { "MultiplayerGame", "EditorGame" })
                    {
                        var type = descriptor.CreateElement("GameType"); type.SetAttribute("value", gameType); types.AppendChild(type);
                    }
                    xmlNode.AppendChild(types); descriptor.SelectSingleNode("/Module/Xmls").AppendChild(xmlNode);
                }
            }
            descriptor.Save(Path.Combine(staged, "SubModule.xml"));
            var lines = new List<string> { CoopCampaignContentProfile.AdapterFormat, source, profile };
            foreach (string file in new[] { Path.Combine(staged, "SubModule.xml") }.Concat(
                Directory.GetFiles(localData, "*", SearchOption.TopDirectoryOnly)).Concat(
                Directory.GetFiles(prepared, "*", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.Ordinal)))
                lines.Add(file.Substring(staged.Length + 1) + "|" + Hash(file));
            File.WriteAllLines(Path.Combine(staged, "warhost-resources.txt"), lines, new UTF8Encoding(false));
        }

        private static void ValidateOwned(string root, string source, bool catalogOnly)
        {
            if (WindowsDirectoryJunction.IsReparsePoint(root)) throw new IOException("Resource adapter root must not be a link: " + root);
            var descriptor = CoopCampaignContentProfile.ReadDescriptor(root, catalogOnly ?
                CoopCampaignContentProfile.NavalClientCatalogId : CoopCampaignContentProfile.NavalAdapterId);
            if (descriptor.SelectNodes("/Module/SubModules/SubModule").Count != 0)
                throw new IOException("Resource adapter must not load gameplay submodules.");
            string marker = Path.Combine(root, "warhost-resources.txt");
            if (!File.Exists(marker) || new FileInfo(marker).Length > 65536) throw new IOException("Foreign adapter directory preserved: " + root);
            string[] lines = File.ReadAllLines(marker);
            if (lines.Length < 4 || lines.Length > 256 || lines[0] != CoopCampaignContentProfile.AdapterFormat ||
                !WindowsDirectoryJunction.SamePath(lines[1], source) || !CoopCampaignContentProfile.IsNaval(lines[2]))
                throw new IOException("Adapter ownership/source identity is invalid: " + root);
            string localData = Path.Combine(root, "ModuleData"), prepared = Path.Combine(localData, "WarhostPrepared");
            if (WindowsDirectoryJunction.IsReparsePoint(localData) || WindowsDirectoryJunction.IsReparsePoint(prepared) ||
                WindowsDirectoryJunction.IsReparsePoint(Path.Combine(prepared, "XSLT")))
                throw new IOException("Adapter private metadata must not be linked.");
            foreach (string directory in Directory.GetDirectories(prepared))
                if (!string.Equals(Path.GetFileName(directory), "XSLT", StringComparison.Ordinal) ||
                    WindowsDirectoryJunction.IsReparsePoint(directory) || Directory.GetDirectories(directory).Length != 0)
                    throw new IOException("Unexpected adapter private metadata directory.");
            var recorded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string line in lines.Skip(3))
            {
                string[] pair = line.Split('|');
                if (pair.Length != 2 || Path.IsPathRooted(pair[0]) || pair[0].Contains("..") ||
                    (pair[0] != "SubModule.xml" && !pair[0].StartsWith("ModuleData\\", StringComparison.Ordinal)))
                    throw new IOException("Invalid adapter file identity.");
                if (!recorded.Add(pair[0])) throw new IOException("Duplicate adapter file identity.");
                string file = Path.Combine(root, pair[0]);
                if (WindowsDirectoryJunction.IsReparsePoint(file) || Hash(file) != pair[1])
                    throw new IOException("Modified adapter data preserved: " + file);
            }
            var actual = new HashSet<string>(new[] { Path.Combine(root, "SubModule.xml") }.Concat(
                Directory.GetFiles(localData, "*", SearchOption.TopDirectoryOnly)).Concat(
                Directory.GetFiles(prepared, "*", SearchOption.AllDirectories)).Select(p => p.Substring(root.Length + 1)),
                StringComparer.OrdinalIgnoreCase);
            if (!actual.SetEquals(recorded)) throw new IOException("Unexpected adapter metadata files preserved.");
            if (catalogOnly && (Directory.GetDirectories(root).Any(p => Path.GetFileName(p) != "ModuleData") ||
                Directory.GetDirectories(localData).Any(p => Path.GetFileName(p) != "WarhostPrepared") ||
                Directory.GetFiles(localData).Length != 0))
                throw new IOException("Client catalog must not duplicate official resources or load bin assemblies.");
            foreach (string name in catalogOnly ? new string[0] : ResourceDirectories)
                if (Directory.Exists(Path.Combine(source, name)) &&
                    (!WindowsDirectoryJunction.IsReparsePoint(Path.Combine(root, name)) ||
                     !WindowsDirectoryJunction.SamePath(WindowsDirectoryJunction.ReadTarget(Path.Combine(root, name)),
                         WindowsDirectoryJunction.ResolveDirectory(Path.Combine(source, name)))))
                    throw new IOException("Adapter resource link mismatch: " + name);
            foreach (string directory in catalogOnly ? new string[0] : Directory.GetDirectories(Path.Combine(source, "ModuleData")))
            {
                string local = Path.Combine(root, "ModuleData", Path.GetFileName(directory));
                if (!WindowsDirectoryJunction.IsReparsePoint(local) ||
                    !WindowsDirectoryJunction.SamePath(WindowsDirectoryJunction.ReadTarget(local), WindowsDirectoryJunction.ResolveDirectory(directory)))
                    throw new IOException("Adapter metadata directory link mismatch: " + local);
            }
        }

        private static string Hash(string file)
        {
            using (var stream = File.OpenRead(file)) using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty);
        }
    }
}
