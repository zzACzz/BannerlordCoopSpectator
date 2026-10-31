using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;

namespace CoopSpectator.Infrastructure
{
    internal sealed class CoopNavalCatalogReadiness
    {
        private readonly string[] _items;
        private readonly string[] _pieces;
        private CoopNavalCatalogReadiness(string[] items, string[] pieces) { _items = items; _pieces = pieces; }

        internal static CoopNavalCatalogReadiness Read(string navalRoot)
        {
            var descriptor = CoopCampaignContentProfile.ReadDescriptor(navalRoot, "NavalDLC");
            var items = new HashSet<string>(StringComparer.Ordinal);
            var pieces = new HashSet<string>(StringComparer.Ordinal);
            foreach (XmlNode entry in descriptor.SelectNodes("/Module/Xmls/XmlNode"))
            {
                string kind = entry.SelectSingleNode("XmlName/@id")?.Value;
                if (kind != "Items" && kind != "CraftingPieces") continue;
                foreach (string file in CoopCampaignContentProfile.ResolveDataFiles(navalRoot, entry.SelectSingleNode("XmlName/@path")?.Value))
                {
                    if (Path.GetExtension(file) != ".xml") continue;
                    var data = CoopCampaignContentProfile.ReadXml(file);
                    foreach (XmlElement node in data.SelectNodes(kind == "Items" ? "/Items/*" : "/CraftingPieces/CraftingPiece"))
                    {
                        string id = node.GetAttribute("id");
                        if (string.IsNullOrEmpty(id)) throw new IOException("Naval catalog contains an item without identity.");
                        (kind == "Items" ? items : pieces).Add(id);
                    }
                }
            }
            if (items.Count == 0 || pieces.Count == 0) throw new IOException("Naval item/crafting catalogs are incomplete.");
            return new CoopNavalCatalogReadiness(items.OrderBy(s => s, StringComparer.Ordinal).ToArray(),
                pieces.OrderBy(s => s, StringComparer.Ordinal).ToArray());
        }

        internal bool Validate(Func<string, bool> itemReady, Func<string, bool> pieceReady, out string reason)
        {
            string[] missingItems = _items.Where(id => !itemReady(id)).ToArray();
            string[] missingPieces = _pieces.Where(id => !pieceReady(id)).ToArray();
            if (missingItems.Length == 0 && missingPieces.Length == 0) { reason = null; return true; }
            reason = "War Sails catalogs were not loaded at startup. Missing items=" + missingItems.Length +
                " [" + string.Join(",", missingItems.Take(5)) + "]; crafting pieces=" + missingPieces.Length +
                " [" + string.Join(",", missingPieces.Take(5)) + "]. Restart through the updated Warhost launcher with War Sails selected.";
            return false;
        }
    }
}
