using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Xml;

namespace CoopSpectator.Infrastructure
{
    // File-only content identity. No game/object registration or native calls.
    public static class CoopCampaignContentProfile
    {
        public const string NavalAdapterId = "CoopSpectatorNavalResources";
        public const string NavalClientCatalogId = "CoopSpectatorNavalCatalog";
        public const string MapVisualAdapterId = "CoopSpectatorMapResources";
        public const string MapVisualSourceRelativePath = "Modules/StoryMode/Prefabs/storymode_prefabs.xml";
        public const string AdapterFormat = "warhost-naval-resources-v1";
        public const int MaxIdLength = 80;
        private static readonly HashSet<string> CatalogKinds = new HashSet<string>(StringComparer.Ordinal) {
            "Items", "CraftingPieces", "CraftingTemplates", "WeaponDescriptions", "SPCultures" };

        public static bool IsValid(string id)
        {
            if (id == null || (id.Length != 72 && id.Length != 73)) return false;
            string prefix = id.StartsWith("base-v1:", StringComparison.Ordinal) ? "base-v1:" :
                id.StartsWith("naval-v1:", StringComparison.Ordinal) ? "naval-v1:" : null;
            if (prefix == null || id.Length != prefix.Length + 64) return false;
            for (int i = prefix.Length; i < id.Length; i++)
                if (!(id[i] >= '0' && id[i] <= '9') && !(id[i] >= 'a' && id[i] <= 'f')) return false;
            return true;
        }

        public static bool IsNaval(string id) => IsValid(id) && id.StartsWith("naval-v1:", StringComparison.Ordinal);

        public static string Compute(string gameRoot, bool naval, string privateModuleData = null)
        {
            gameRoot = Path.GetFullPath(gameRoot);
            var records = new List<string> { AdapterFormat, naval ? "naval" : "base" };
            AddFile(records, gameRoot, Path.Combine(gameRoot, MapVisualSourceRelativePath), "warhost/map-visuals/storymode_prefabs.xml");
            foreach (string module in naval ? new[] { "Native", "SandBoxCore", "Sandbox", "NavalDLC" } :
                new[] { "Native", "SandBoxCore", "Sandbox" })
            {
                string root = Path.Combine(gameRoot, "Modules", module);
                XmlDocument descriptor = ReadDescriptor(root, module);
                AddFile(records, gameRoot, Path.Combine(root, "SubModule.xml"));
                foreach (XmlNode entry in descriptor.SelectNodes("/Module/Xmls/XmlNode"))
                {
                    string kind = entry.SelectSingleNode("XmlName/@id")?.Value;
                    if (!CatalogKinds.Contains(kind ?? string.Empty)) continue;
                    string relative = entry.SelectSingleNode("XmlName/@path")?.Value;
                    foreach (string file in ResolveDataFiles(root, relative)) AddFile(records, gameRoot, file);
                }
            }
            string map = Path.Combine(gameRoot, "Modules", naval ? "NavalDLC" : "Sandbox", "SceneObj", "Main_map");
            if (!File.Exists(Path.Combine(map, "scene.xscene"))) throw new IOException("Campaign map scene is missing: " + map);
            string[] mapFiles = Directory.GetFiles(map, "*", SearchOption.TopDirectoryOnly);
            if (mapFiles.Length > 32) throw new IOException("Unexpected campaign map file count.");
            foreach (string file in mapFiles.OrderBy(p => p, StringComparer.OrdinalIgnoreCase)) AddFile(records, gameRoot, file);
            if (naval)
            {
                foreach (string file in Directory.GetFiles(Path.Combine(gameRoot, "Modules", "NavalDLC", "ModuleData"), "*", SearchOption.TopDirectoryOnly)
                    .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)) AddFile(records, gameRoot, file);
                foreach (string assembly in new[] { "NavalDLC.dll", "NavalDLC.View.dll" })
                    AddFile(records, gameRoot, Path.Combine(gameRoot, "Modules", "NavalDLC", "bin", "Win64_Shipping_Client", assembly));
            }
            string ownData = privateModuleData ?? Path.Combine(gameRoot, "Modules", "CoopSpectator", "ModuleData");
            foreach (string file in new[] { "coopspectator_crafting_pieces.xml", "coopspectator_items.xml", "coopspectator_campaign_mirror_items.xml" })
                AddFile(records, gameRoot, Path.Combine(ownData, file), "warhost/catalogs/" + file);
            using (var sha = SHA256.Create())
                return (naval ? "naval-v1:" : "base-v1:") + Hex(sha.ComputeHash(Encoding.UTF8.GetBytes(string.Join("\n", records))));
        }

        internal static XmlDocument ReadDescriptor(string moduleRoot, string expectedId)
        {
            var document = ReadXml(Path.Combine(moduleRoot, "SubModule.xml"));
            if (!string.Equals(document.SelectSingleNode("/Module/Id/@value")?.Value, expectedId, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Unexpected module identity: " + moduleRoot);
            return document;
        }

        internal static XmlDocument ReadXml(string path)
        {
            var document = new XmlDocument { XmlResolver = null };
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null,
                MaxCharactersInDocument = 32 * 1024 * 1024 };
            using (var reader = XmlReader.Create(path, settings)) document.Load(reader);
            return document;
        }

        internal static string[] ResolveDataFiles(string moduleRoot, string relative)
        {
            if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.Contains(".."))
                throw new IOException("Invalid official catalog path.");
            string path = Path.Combine(moduleRoot, "ModuleData", relative.Replace('/', Path.DirectorySeparatorChar));
            if (Directory.Exists(path))
            {
                string[] files = Directory.GetFiles(path, "*.xml", SearchOption.TopDirectoryOnly);
                if (files.Length > 128) throw new IOException("Unexpected catalog directory size.");
                return files.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray();
            }
            if (File.Exists(path + ".xml")) return new[] { path + ".xml" };
            if (File.Exists(path + ".xslt")) return new[] { path + ".xslt" };
            throw new FileNotFoundException("Official catalog dependency is missing.", path);
        }

        private static void AddFile(List<string> records, string root, string path, string label = null)
        {
            var info = new FileInfo(path);
            long length = info.Length; DateTime stamp = info.LastWriteTimeUtc;
            if (length > 128L * 1024 * 1024) throw new IOException("Content identity input is too large: " + path);
            string hash;
            using (var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var sha = SHA256.Create()) hash = Hex(sha.ComputeHash(stream));
            info.Refresh();
            if (length != info.Length || stamp != info.LastWriteTimeUtc) throw new IOException("Game content changed during verification: " + path);
            records.Add((label ?? path.Substring(root.TrimEnd('\\', '/').Length).Replace('\\', '/').ToLowerInvariant()) + "=" + hash);
        }

        private static string Hex(byte[] bytes) => BitConverter.ToString(bytes).Replace("-", string.Empty).ToLowerInvariant();
    }
}
