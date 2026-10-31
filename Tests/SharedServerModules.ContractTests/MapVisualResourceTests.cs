using System;
using System.IO;
using System.Linq;
using CoopSpectator.DedicatedHelper;
using CoopSpectator.Infrastructure;

internal static class MapVisualResourceTests
{
    internal static void Seed(string game)
    {
        string story = Path.Combine(game, "Modules", "StoryMode");
        Directory.CreateDirectory(Path.Combine(story, "Prefabs"));
        File.WriteAllText(Path.Combine(story, "SubModule.xml"), "<Module><Id value='StoryMode'/></Module>");
        File.WriteAllText(Path.Combine(game, CoopCampaignContentProfile.MapVisualSourceRelativePath),
            "<prefabs><game_entity name='tutorial_training_field'><transform rotation_euler='0, 0, -2.525'/>" +
            "<children><game_entity name='training'><components><meta_mesh_component name='mi_training_01'/></components>" +
            "</game_entity></children></game_entity><game_entity name='unrelated'/></prefabs>");
    }

    internal static void Run(string game, string root)
    {
        string server = Path.Combine(root, "Map visual server"), source = Path.Combine(game, CoopCampaignContentProfile.MapVisualSourceRelativePath);
        Directory.CreateDirectory(Path.Combine(server, "Modules"));
        string before = File.ReadAllText(source), baseId = CoopCampaignContentProfile.Compute(game, false), navalId = CoopCampaignContentProfile.Compute(game, true);
        string hash = CoopMapVisualResourceAdapter.Prepare(game, server);
        string adapter = Path.Combine(server, "Modules", CoopCampaignContentProfile.MapVisualAdapterId);
        string prefab = Path.Combine(adapter, "Prefabs", "training_field.xml");
        var projected = CoopCampaignContentProfile.ReadXml(prefab);
        var original = CoopCampaignContentProfile.ReadXml(source);
        Check(projected.SelectNodes("/prefabs/game_entity").Count == 1 &&
            projected.SelectSingleNode("/prefabs/game_entity").OuterXml == original.SelectSingleNode("/prefabs/game_entity[@name='tutorial_training_field']").OuterXml,
            "exact prefab subtree, authored rotation and children preserved; unrelated prefab excluded");
        var descriptor = CoopCampaignContentProfile.ReadDescriptor(adapter, CoopCampaignContentProfile.MapVisualAdapterId);
        Check(descriptor.SelectNodes("/Module/SubModules/SubModule").Count == 0 && descriptor.SelectNodes("/Module/Xmls/XmlNode").Count == 0 &&
            Directory.GetFiles(adapter, "*", SearchOption.AllDirectories).Length == 3 &&
            !Directory.Exists(Path.Combine(adapter, "bin")) && !Directory.Exists(Path.Combine(adapter, "SceneObj")), "resource-only bounded module");
        DateTime stamp = File.GetLastWriteTimeUtc(prefab);
        Check(CoopMapVisualResourceAdapter.Prepare(game, server) == hash && File.GetLastWriteTimeUtc(prefab) == stamp && File.ReadAllText(source) == before,
            "verified reuse does not rewrite source or projection");
        File.AppendAllText(source, "\n");
        Check(CoopCampaignContentProfile.Compute(game, false) != baseId && CoopCampaignContentProfile.Compute(game, true) != navalId,
            "map visual source participates in both campaign profiles");
        Check(CoopMapVisualResourceAdapter.Prepare(game, server) != hash && Directory.GetDirectories(Path.Combine(server, "CoopSpectator.SharedModuleBackups"))
            .Any(p => File.Exists(Path.Combine(p, "previous-adapter", "Prefabs", "training_field.xml"))), "updated source preserves prior adapter");
        File.AppendAllText(prefab, "\n");
        Reject(() => CoopMapVisualResourceAdapter.Prepare(game, server), "modified projection must be preserved");
        string foreign = Path.Combine(root, "Foreign map server");
        Directory.CreateDirectory(Path.Combine(foreign, "Modules", CoopCampaignContentProfile.MapVisualAdapterId));
        Reject(() => CoopMapVisualResourceAdapter.Prepare(game, foreign), "foreign adapter must be preserved");
        string unsafeServer = Path.Combine(root, "Unsafe source server"); Directory.CreateDirectory(Path.Combine(unsafeServer, "Modules"));
        File.WriteAllText(source, before.Replace("<children>", "<scripts><script/></scripts><children>"));
        Reject(() => CoopMapVisualResourceAdapter.Prepare(game, unsafeServer), "scripted source must never be enabled");
        Check(!Directory.Exists(Path.Combine(unsafeServer, "Modules", CoopCampaignContentProfile.MapVisualAdapterId)), "failed source not activated");
        File.WriteAllText(source, before);
        Console.WriteLine("PASS exact training prefab resource projection, reuse, both content profiles, preservation and script rejection.");
    }

    private static void Reject(Action action, string message)
    { try { action(); } catch (IOException) { return; } throw new InvalidOperationException(message); }
    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}
