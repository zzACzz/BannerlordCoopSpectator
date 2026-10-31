using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;

namespace CoopSpectator.Infrastructure
{
    // The official tileset catalog identifies the resource templates whose
    // virtual-texture binding was computed before the alias was activated.
    internal static class CoopNavalMaterialCatalog
    {
        internal static IReadOnlyList<string> ReadNames(string path)
        {
            var settings = new XmlReaderSettings {
                DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null,
                MaxCharactersInDocument = 2 * 1024 * 1024
            };
            var document = new XmlDocument { XmlResolver = null };
            using (var reader = XmlReader.Create(path, settings)) document.Load(reader);
            if (document.DocumentElement?.Name != "AssetImport" || document.SelectSingleNode("/AssetImport/Imports") == null)
                throw new InvalidDataException("Naval material catalog has an unexpected root.");
            var names = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            XmlNodeList imports = document.SelectNodes("/AssetImport/Imports/Import");
            if (imports.Count == 0 || imports.Count > 4096)
                throw new InvalidDataException("Naval material catalog exceeds its import budget.");
            foreach (XmlElement import in imports)
            {
                string secondary = import.GetAttribute("Secondary");
                if (secondary == "1") continue; // Secondary entries are not separate material resources.
                if (secondary != "0") throw new InvalidDataException("Unknown Naval material import kind.");
                string name = import.GetAttribute("Name");
                if (string.IsNullOrWhiteSpace(name) || name.Length > 256 || name.IndexOfAny(new[] { '/', '\\', '\r', '\n', '\t' }) >= 0)
                    throw new InvalidDataException("Invalid Naval material resource name.");
                if (seen.Add(name)) names.Add(name);
            }
            if (names.Count == 0) throw new InvalidDataException("Naval material catalog has no primary imports.");
            return names;
        }
    }
}
