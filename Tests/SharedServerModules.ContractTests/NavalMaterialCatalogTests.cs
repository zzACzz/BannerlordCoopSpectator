using System;
using System.IO;
using System.Linq;
using System.Xml;
using CoopSpectator.Infrastructure;

internal static class NavalMaterialCatalogTests
{
    internal static void Run()
    {
        string root = Path.Combine(Path.GetTempPath(), "CoopNavalMaterialCatalog-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string file = Path.Combine(root, "ValidMaterials.xml");
        try
        {
            File.WriteAllText(file, "<AssetImport><Imports><Import Name='boots' Secondary='0'/>" +
                "<Import Name='boots' Secondary='0'/><Import Name='tunic__secondary_' Secondary='1'/>" +
                "<Import Name='tunic' Secondary='0'/></Imports></AssetImport>");
            Check(CoopNavalMaterialCatalog.ReadNames(file).SequenceEqual(new[] { "boots", "tunic" }),
                "Only distinct primary material resources, preserving authored order");
            foreach (string xml in new[] {
                "<unrelated/>", "<AssetImport><Imports/></AssetImport>",
                "<AssetImport><Imports><Import Name='a' Secondary='2'/></Imports></AssetImport>",
                "<AssetImport><Imports><Import Name='' Secondary='0'/></Imports></AssetImport>",
                "<AssetImport><Imports><Import Name='../foreign' Secondary='0'/></Imports></AssetImport>",
                "<AssetImport><Imports><Import Name='secondary' Secondary='1'/></Imports></AssetImport>",
                "<!DOCTYPE AssetImport [<!ENTITY data SYSTEM 'file:///never-read'>]><AssetImport><Imports>&data;</Imports></AssetImport>",
                "<AssetImport><Imports>" + string.Concat(Enumerable.Repeat("<Import Name='a' Secondary='0'/>", 4097)) + "</Imports></AssetImport>",
                "<AssetImport><Imports>" + new string(' ', 2 * 1024 * 1024) + "</Imports></AssetImport>"
            })
            {
                File.WriteAllText(file, xml);
                bool rejected = false;
                try { CoopNavalMaterialCatalog.ReadNames(file); }
                catch (InvalidDataException) { rejected = true; }
                catch (XmlException) { rejected = true; }
                Check(rejected, "Malformed, external-entity and oversized catalogs refused before native calls");
            }
            Console.WriteLine("PASS Naval material catalog: distinct primary resources, XML/import/name budgets and external-entity rejection.");
        }
        finally { File.Delete(file); Directory.Delete(root, false); }
    }
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
