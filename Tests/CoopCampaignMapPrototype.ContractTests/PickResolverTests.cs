using System;
using System.IO;
using CoopSpectator.Infrastructure;

internal static class PickResolverTests
{
    public static void Run()
    {
        var resolver = new CoopCampaignMapPickResolver();
        var a = new UIntPtr(1); var b = new UIntPtr(2); var c = new UIntPtr(3);
        resolver.Register(a, "settlement:a", false);
        resolver.Register(b, "settlement:b", false);
        resolver.Register(c, "party:c", true);
        UIntPtr[] hits = { a, b, c };
        Check(resolver.Resolve(hits, 2, _ => true, _ => true) == "settlement:b", "Native reverse settlement hover order");
        Check(resolver.Resolve(hits, 3, _ => true, _ => true) == "party:c", "Mobile manager has priority");
        Check(resolver.Resolve(hits, 3, id => id != "party:c", _ => true) == "settlement:b", "Undisclosed mobile is excluded");
        Check(resolver.Resolve(hits, 3, _ => true, handle => handle == a) == "settlement:a", "Hidden geometry is excluded");
        int nativeReads = 0;
        Check(resolver.Resolve(hits, 3, _ => false, _ => { nativeReads++; return true; }) == null && nativeReads == 0,
            "Do not dereference undisclosed geometry");
        resolver.Register(a, "settlement:conflict", false);
        Check(resolver.Resolve(new[] { a }, 1, _ => true, _ => true) == null, "Conflicting handle is rejected");
        resolver.Register(a, "settlement:a", false);
        Check(resolver.Resolve(new[] { a }, 1, _ => true, _ => true) == null, "Conflict cannot self-heal by re-registration");
        resolver.Remove(a);
        resolver.Register(a, "party:a", true);
        Check(resolver.Resolve(hits, 3, _ => true, _ => true) == "party:a", "Native mobile overwrite order");
        resolver.Remove(a);
        Check(resolver.Resolve(hits, 3, _ => true, _ => true) == "party:c", "Destroyed body binding cannot win");
        Check(resolver.Resolve(hits, -1, _ => true, _ => true) == null &&
            resolver.Resolve(hits, 4, _ => true, _ => true) == null &&
            resolver.Resolve(new UIntPtr[129], 129, _ => true, _ => true) == null, "Native count bounds");
        resolver.Clear();
        Check(resolver.Resolve(hits, 3, _ => true, _ => true) == null, "New scene has no stale bindings");
        resolver.Register(c, "settlement:new", false);
        Check(resolver.Resolve(hits, 3, _ => true, _ => true) == "settlement:new", "New scene identity is independent");

        string root = Directory.GetCurrentDirectory();
        while (!File.Exists(Path.Combine(root, "CoopSpectator.csproj")))
            root = Directory.GetParent(root)?.FullName ?? throw new Exception("Repository not found");
        string view = File.ReadAllText(Path.Combine(root, "UI", "CoopCampaignMapPrototypeMissionView.cs"));
        string picking = File.ReadAllText(Path.Combine(root, "UI", "CoopCampaignMapPrototypeMissionView.Picking.cs"));
        Check(view.Contains("string hoveredId = ResolveNativeHoveredReplica();") && !view.Contains(".SelectionRadiusPixels"),
            "No screen-radius fallback can select nearby empty space");
        Check(picking.Contains("_mapScene.SelectEntitiesCollidedWith") && picking.Contains("TranslateMouse(ref near, ref far)") &&
            picking.Contains("BodyFlags.Moveable | BodyFlags.OnlyCollideWithRaycast") && picking.Contains("Vec3.Zero, 0.5f"),
            "Native ray query and stock party sphere wired");
        Check(picking.Contains("dynamicState.IsVisible") && picking.Contains("ShouldRenderBody") &&
            picking.Contains("entity.IsVisibleIncludeParents()"), "Disclosure and rendered geometry are both required");
        Check(view.Contains("ClearNativePicking();") && view.Contains("RemovePartyPickGeometry(replica);") &&
            picking.Contains("Array.Clear(_pickHits") && picking.Contains("_pickResolver.Clear()"),
            "Owned scene release and stale hit clearing are wired");
        Check(picking.Contains("!replica.PickFrame.NearlyEquals(frame") && picking.Contains("replica.HasPickFrame = false"),
            "Unchanged frame does not mutate native physics and removal invalidates cache");
        Console.WriteLine("Pick resolver contracts passed (17 checks).");
    }
    private static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
}
