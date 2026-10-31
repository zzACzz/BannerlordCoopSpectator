using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using CoopSpectator.Infrastructure;

namespace CoopSpectator.DedicatedHelper
{
    // Project only the small official training-field prefab. No StoryMode
    // assembly, gameplay component, scene or heavy asset is copied/enabled.
    public static class CoopMapVisualResourceAdapter
    {
        private const string Format = "warhost-map-visuals-v1";
        private const string Marker = "warhost-map-visuals.txt";
        private const string Prefab = "tutorial_training_field";
        private const string PrefabFile = "training_field.xml";

        public static string Prepare(string gameRoot, string installationRoot)
        {
            gameRoot = WindowsDirectoryJunction.ResolveDirectory(gameRoot);
            installationRoot = WindowsDirectoryJunction.ResolveDirectory(installationRoot);
            string source = Path.Combine(gameRoot, CoopCampaignContentProfile.MapVisualSourceRelativePath);
            CoopCampaignContentProfile.ReadDescriptor(Path.Combine(gameRoot, "Modules", "StoryMode"), "StoryMode");
            string sourceHash = Hash(source);
            string modules = Path.Combine(installationRoot, "Modules");
            if (!Directory.Exists(modules) || WindowsDirectoryJunction.IsReparsePoint(modules))
                throw new IOException("Map visuals require an ordinary Modules directory.");
            string target = Path.Combine(modules, CoopCampaignContentProfile.MapVisualAdapterId);
            using (var fileLock = new FileStream(Path.Combine(installationRoot, "CoopSpectator.map-visuals.lock"),
                FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
            {
                if (WindowsDirectoryJunction.Exists(target) && ValidateOwned(target, source)[2] == sourceHash)
                    return sourceHash;
                string backups = Path.Combine(installationRoot, "CoopSpectator.SharedModuleBackups");
                if (WindowsDirectoryJunction.Exists(backups) && WindowsDirectoryJunction.IsReparsePoint(backups))
                    throw new IOException("Map visual backups cannot use an unknown link.");
                string transaction = Path.Combine(backups, "MapVisuals-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff") + "-" + Guid.NewGuid().ToString("N"));
                string staged = Path.Combine(transaction, CoopCampaignContentProfile.MapVisualAdapterId + ".pending");
                string prior = Path.Combine(transaction, "previous-adapter");
                // Verify the absolute move boundaries before touching either tree.
                if (!Path.GetFullPath(target).StartsWith(Path.GetFullPath(modules) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                    !Path.GetFullPath(staged).StartsWith(Path.GetFullPath(backups) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("Invalid map visual transaction boundary.");
                Directory.CreateDirectory(staged);
                bool moved = false, installed = false;
                try
                {
                    Build(staged, source, sourceHash); ValidateOwned(staged, source);
                    if (Directory.Exists(target)) { Directory.Move(target, prior); moved = true; }
                    Directory.Move(staged, target); installed = true;
                    File.WriteAllText(Path.Combine(transaction, "RESTORE.txt"), "State=committed\nAdapter=" + target +
                        "\nSource=" + source + "\nPrevious=" + (moved ? prior : "none") + "\n", new UTF8Encoding(false));
                    return sourceHash;
                }
                catch
                {
                    if (installed) { ValidateOwned(target, source); Directory.Move(target, staged); }
                    if (moved && !WindowsDirectoryJunction.Exists(target)) Directory.Move(prior, target);
                    throw;
                }
            }
        }

        private static void Build(string staged, string source, string sourceHash)
        {
            XmlDocument original = CoopCampaignContentProfile.ReadXml(source);
            XmlNode root = original.SelectSingleNode("/prefabs/game_entity[@name='" + Prefab + "']");
            if (root == null || original.SelectNodes("/prefabs/game_entity[@name='" + Prefab + "']").Count != 1 ||
                root.SelectNodes(".//game_entity").Count > 128 || root.SelectNodes(".//*[contains(local-name(), 'script')]").Count != 0)
                throw new IOException("Unsupported official training-field prefab contract.");
            var document = new XmlDocument { XmlResolver = null };
            document.AppendChild(document.CreateElement("prefabs")).AppendChild(document.ImportNode(root, true));
            string folder = Path.Combine(staged, "Prefabs"); Directory.CreateDirectory(folder);
            string prefab = Path.Combine(folder, PrefabFile); Save(document, prefab);
            var descriptor = new XmlDocument { XmlResolver = null };
            descriptor.LoadXml("<Module><Name value='Warhost map visuals'/><Id value='" + CoopCampaignContentProfile.MapVisualAdapterId +
                "'/><Version value='v1.0.0'/><SingleplayerModule value='false'/><MultiplayerModule value='true'/>" +
                "<DependedModules><DependedModule Id='Native'/><DependedModule Id='SandBoxCore'/><DependedModule Id='Sandbox'/>" +
                "<DependedModule Id='Multiplayer'/></DependedModules><SubModules/><Xmls/></Module>");
            string module = Path.Combine(staged, "SubModule.xml"); Save(descriptor, module);
            if (Hash(source) != sourceHash) throw new IOException("Official map prefab changed during preparation.");
            File.WriteAllLines(Path.Combine(staged, Marker), new[] { Format, source, sourceHash, Hash(prefab), Hash(module) }, new UTF8Encoding(false));
        }

        private static string[] ValidateOwned(string root, string source)
        {
            string folder = Path.Combine(root, "Prefabs"), marker = Path.Combine(root, Marker);
            if (WindowsDirectoryJunction.IsReparsePoint(root) || !File.Exists(marker) || new FileInfo(marker).Length > 4096 ||
                !Directory.Exists(folder) || WindowsDirectoryJunction.IsReparsePoint(folder) ||
                Directory.GetDirectories(root).Length != 1 || Directory.GetDirectories(folder).Length != 0 ||
                Directory.GetFiles(root).Length != 2 || Directory.GetFiles(folder).Length != 1)
                throw new IOException("Unowned or modified map visual adapter; preserved.");
            string[] lines = File.ReadAllLines(marker);
            if (lines.Length != 5 || lines[0] != Format || !WindowsDirectoryJunction.SamePath(lines[1], source) ||
                lines[3] != Hash(Path.Combine(folder, PrefabFile)) || lines[4] != Hash(Path.Combine(root, "SubModule.xml")))
                throw new IOException("Map visual adapter ownership/integrity failed; preserved.");
            var descriptor = CoopCampaignContentProfile.ReadDescriptor(root, CoopCampaignContentProfile.MapVisualAdapterId);
            if (descriptor.SelectNodes("/Module/SubModules/SubModule").Count != 0 || descriptor.SelectNodes("/Module/Xmls/XmlNode").Count != 0)
                throw new IOException("Map visuals cannot contain gameplay submodules or catalogs.");
            return lines;
        }

        private static string Hash(string file)
        {
            if (!File.Exists(file) || new FileInfo(file).Length > 1024 * 1024 || WindowsDirectoryJunction.IsReparsePoint(file))
                throw new IOException("Map visual metadata is missing, linked or exceeds its bounded budget: " + file);
            using (var stream = File.OpenRead(file))
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }

        private static void Save(XmlDocument document, string path)
        {
            using (var writer = XmlWriter.Create(path, new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = true }))
                document.Save(writer);
        }
    }
}
