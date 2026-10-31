using System;
using System.IO;
using System.Linq;
using CoopSpectator.DedicatedHelper;
using CoopSpectator.Infrastructure;

internal static class NavalResourceTests
{
    internal static void Run()
    {
        string root = Path.Combine(Path.GetTempPath(), "WarhostNavalContract-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string game = Path.Combine(root, "Game with spaces"), server = Path.Combine(root, "Server");
            Directory.CreateDirectory(Path.Combine(server, "Modules"));
            foreach (string id in new[] { "Native", "SandBoxCore", "Sandbox", "NavalDLC" })
            {
                string module = Path.Combine(game, "Modules", id);
                Directory.CreateDirectory(Path.Combine(module, "ModuleData"));
                Directory.CreateDirectory(Path.Combine(module, "SceneObj", "Main_map"));
                Directory.CreateDirectory(Path.Combine(module, "AssetPackages"));
                File.WriteAllText(Path.Combine(module, "SceneObj", "Main_map", "scene.xscene"), "map-" + id);
                File.WriteAllText(Path.Combine(module, "SubModule.xml"), "<Module><Id value='" + id +
                    "'/><Xmls><XmlNode><XmlName id='Items' path='items'/></XmlNode>" +
                    (id == "NavalDLC" ? "<XmlNode><XmlName id='CraftingPieces' path='naval_crafting_pieces'/></XmlNode>" : "") + "</Xmls></Module>");
                File.WriteAllText(Path.Combine(module, "ModuleData", "items.xml"), "<Items><Item id='" + id + "_armor' Type='BodyArmor'/></Items>");
            }
            MapVisualResourceTests.Seed(game);
            string naval = Path.Combine(game, "Modules", "NavalDLC");
            string ownData = Path.Combine(game, "Modules", "CoopSpectator", "ModuleData"); Directory.CreateDirectory(ownData);
            foreach (string name in new[] { "coopspectator_crafting_pieces.xml", "coopspectator_items.xml", "coopspectator_campaign_mirror_items.xml" })
                File.WriteAllText(Path.Combine(ownData, name), "<Items/>");
            string piece = Path.Combine(naval, "ModuleData", "naval_crafting_pieces.xml");
            File.WriteAllText(piece, "<CraftingPieces><CraftingPiece id='naval_axe_head_08'/></CraftingPieces>");
            var readiness = CoopNavalCatalogReadiness.Read(naval);
            Check(!readiness.Validate(id => true, id => false, out var readinessReason) && readinessReason.Contains("naval_axe_head_08"),
                "a large unrelated crafting catalog cannot hide the exact missing Naval dependency");
            Check(readiness.Validate(id => id == "NavalDLC_armor", id => id == "naval_axe_head_08", out _),
                "all exact preloaded Naval identities admitted without registering anything");
            string bin = Path.Combine(naval, "bin", "Win64_Shipping_Client"); Directory.CreateDirectory(bin);
            File.WriteAllText(Path.Combine(bin, "NavalDLC.dll"), "fixture core assembly");
            File.WriteAllText(Path.Combine(bin, "NavalDLC.View.dll"), "fixture view assembly");
            string baseId = CoopCampaignContentProfile.Compute(game, false), navalId = CoopCampaignContentProfile.Compute(game, true);
            Check(CoopCampaignContentProfile.IsValid(baseId) && CoopCampaignContentProfile.IsNaval(navalId) && baseId != navalId,
                "base/Naval identities are explicit and bounded");
            Check(!CoopCampaignContentProfile.IsValid("naval-v1:" + new string('g',64)) &&
                !CoopCampaignContentProfile.IsValid(navalId + "\n"), "malformed identity rejected");
            string sourceXml = File.ReadAllText(Path.Combine(naval, "ModuleData", "items.xml"));
            Check(CoopNavalResourceAdapter.Prepare(game, server) == navalId, "adapter prepared from current game source");
            string adapter = Path.Combine(server, "Modules", CoopCampaignContentProfile.NavalAdapterId);
            var descriptor = CoopCampaignContentProfile.ReadDescriptor(adapter, CoopCampaignContentProfile.NavalAdapterId);
            Check(descriptor.SelectNodes("/Module/SubModules/SubModule").Count == 0 && !Directory.Exists(Path.Combine(adapter, "bin")),
                "no official campaign DLL/submodule deployment");
            Check(WindowsDirectoryJunction.SamePath(WindowsDirectoryJunction.ReadTarget(Path.Combine(adapter, "SceneObj")), Path.Combine(naval, "SceneObj")),
                "scene resources shared through exact junction");
            Check(File.ReadAllText(Path.Combine(adapter, "ModuleData", "WarhostPrepared", "catalog-0.xml")).Contains("multiplayer_item=\"true\"") &&
                File.ReadAllText(Path.Combine(naval, "ModuleData", "items.xml")) == sourceXml,
                "MP admission is prepared locally without changing official items");
            Check(File.ReadAllText(Path.Combine(adapter, "ModuleData", "WarhostPrepared", "catalog-1.xml")).Contains("naval_axe_head_08"),
                "exact missing crafted dependency retained");
            Check(CoopNavalResourceAdapter.PrepareClient(game, game) == navalId, "client catalog preserves server/campaign content identity");
            string client = Path.Combine(game, "Modules", CoopCampaignContentProfile.NavalClientCatalogId);
            CoopNavalResourceAdapter.ValidateClientCatalog(game, client);
            Check(!Directory.Exists(Path.Combine(client, "SceneObj")) && !Directory.Exists(Path.Combine(client, "AssetPackages")) &&
                !Directory.Exists(Path.Combine(client, "TileSets")) && !Directory.Exists(Path.Combine(client, "bin")),
                "canonical client catalog never duplicates official scene, package, texture or assembly ownership");
            DateTime clientStamp = File.GetLastWriteTimeUtc(Path.Combine(client, "SubModule.xml"));
            Check(CoopNavalResourceAdapter.PrepareClient(game, game) == navalId &&
                File.GetLastWriteTimeUtc(Path.Combine(client, "SubModule.xml")) == clientStamp, "client catalog reuse is read-only");
            Directory.CreateDirectory(Path.Combine(client, "SceneObj"));
            Reject(() => CoopNavalResourceAdapter.ValidateClientCatalog(game, client), "client catalog with extra native resources refused");
            Directory.Delete(Path.Combine(client, "SceneObj"));
            DateTime stamp = File.GetLastWriteTimeUtc(Path.Combine(adapter, "SubModule.xml"));
            Check(CoopNavalResourceAdapter.Prepare(game, server) == navalId && File.GetLastWriteTimeUtc(Path.Combine(adapter, "SubModule.xml")) == stamp,
                "same profile reuses verified adapter without rebuilding");
            File.AppendAllText(piece, "\n");
            Check(CoopCampaignContentProfile.Compute(game, false) == baseId && CoopCampaignContentProfile.Compute(game, true) != navalId,
                "inactive DLC changes do not enable/change base profile; active dependencies change identity");
            string updated = CoopNavalResourceAdapter.Prepare(game, server);
            Check(updated != navalId && Directory.GetDirectories(Path.Combine(server, "CoopSpectator.SharedModuleBackups"))
                .Any(p => Directory.Exists(Path.Combine(p, "previous-adapter"))), "profile update preserves old adapter outside Modules");
            File.AppendAllText(Path.Combine(adapter, "SubModule.xml"), "\n");
            Reject(() => CoopNavalResourceAdapter.Prepare(game, server), "modified adapter is preserved and rejected");
            string other = Path.Combine(root, "Foreign server"); Directory.CreateDirectory(Path.Combine(other, "Modules", CoopCampaignContentProfile.NavalAdapterId));
            File.WriteAllText(Path.Combine(other, "Modules", CoopCampaignContentProfile.NavalAdapterId, "SubModule.xml"), "<Module><Id value='foreign'/></Module>");
            Reject(() => CoopNavalResourceAdapter.Prepare(game, other), "foreign folder never overwritten");
            MapVisualResourceTests.Run(game, root);
            Console.WriteLine("PASS Naval resource profile: source identity, exact junctions, MP XML, no campaign submodules, reuse and preservation; no game started.");
        }
        finally
        {
            UnlinkOwnedTree(root, root);
            Directory.Delete(root, true);
        }
    }

    private static void UnlinkOwnedTree(string directory, string root)
    {
        foreach (string child in Directory.GetDirectories(directory))
        {
            if (WindowsDirectoryJunction.IsReparsePoint(child))
            {
                string target = WindowsDirectoryJunction.ReadTarget(child);
                if (!Path.GetFullPath(target).StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("Refuse scratch cleanup of a foreign resource link.");
                WindowsDirectoryJunction.RemoveExact(child, target);
            }
            else UnlinkOwnedTree(child, root);
        }
    }
    private static void Reject(Action action, string message)
    { try { action(); } catch (IOException) { return; } throw new InvalidOperationException(message); }
    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}
